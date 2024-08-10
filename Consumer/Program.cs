using Confluent.Kafka;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Globalization;
using Microsoft.VisualBasic;

class Program
{
    static async Task Main(string[] args)
    {
        ConfigReader configReader = InitConfigReader();

        //Kafka Consumer initialization
        ConsumerConfig kafkaConsumerConfig = new ConsumerConfig
        {
            BootstrapServers = configReader.KafkaServerInfo,
            GroupId = configReader.KafkaGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using (var consumer = new ConsumerBuilder<Ignore, string>(kafkaConsumerConfig).Build())
        {
            try
            {
                //Connect to Kafka topic
                consumer.Subscribe(configReader.KafkaTopic);
                
                //MongoDB connection initialization
                MongoClient mongoClient = new MongoClient(configReader.MongoConnectionString);
                var eventsDb = mongoClient.GetDatabase(configReader.MongoDbName);
                var eventsCollection = eventsDb.GetCollection<BsonDocument>(configReader.MongoCollectionName);

                //Read a messege from kafka topic, if exists convert to Binary JSON object and send to mongo db
                while (true)
                {
                    var consumeResult = consumer.Consume();
                    Console.WriteLine($"Recieved from kafka: {consumeResult.Message.Value}");
                        
                    BsonDocument eventDocument = ConvertToBSON(configReader,consumeResult.Message.Value);
                    await eventsCollection.InsertOneAsync(eventDocument);

                    Console.WriteLine($"Sent to db: {eventDocument}");
  
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                consumer.Close();
            }
        }
    }

    private static ConfigReader InitConfigReader()
    {
        string currentDirectory = Directory.GetCurrentDirectory();   
        string configFilePath = Path.Combine(currentDirectory,"..","config.yml");
        ConfigReader configReader = new ConfigReader(configFilePath);
        return configReader;
    }

    //Takes a JSON string and converts it to a BSON object
    //Attempts to convert string timestamp entery into a DateTime object with defind time format
    private static BsonDocument ConvertToBSON(ConfigReader configReader, string jsonString) 
    {
        string? timeStringFormat = configReader.TimeFormat;

        BsonDocument eventDocument = BsonDocument.Parse(jsonString);

        string timestampString = eventDocument["Timestamp"].AsString;

        if (DateTimeOffset.TryParseExact(timestampString, timeStringFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
        {
            // Convert to UTC DateTime for MongoDB
            eventDocument["Timestamp"] = new BsonDateTime(result.UtcDateTime);
            Console.WriteLine("Timestamp converted: " + eventDocument["Timestamp"]);
        }
        else
        {
            Console.WriteLine("Timestamp conversion failed.");
        }
    
        return eventDocument;
    }
}

