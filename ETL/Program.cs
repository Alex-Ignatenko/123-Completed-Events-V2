using NRedisStack;
using NRedisStack.RedisStackCommands;
using StackExchange.Redis;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Globalization;

class Program
{
    static async Task Main(string[] args)
    {
        ConfigReader configReader = InitConfigReader();

        try
        {
            //MongoDB connection initialization
            MongoClient mongoClient = new MongoClient(configReader.MongoConnectionString);
            var eventsDb = mongoClient.GetDatabase(configReader.MongoDbName);
            var eventsCollection = eventsDb.GetCollection<BsonDocument>(configReader.MongoCollectionName);

            //Redis connection initialization 
            ConnectionMultiplexer redisConnection = ConnectionMultiplexer.Connect(configReader.REDISConnectionString!);
            IDatabase redisDb = redisConnection.GetDatabase();
            
            while (true)
            {   
                Console.WriteLine("-----------------------------------------------------------ETL woke up...-----------------------------------------------------------");
                //Get the latest timestamp saved in REDIS
                string? timestampFromRedis = redisDb.StringGet("Last Timestamp");

                //Set sort and filter parameters
                SortDefinition<BsonDocument> sortParam = Builders<BsonDocument>.Sort.Ascending("Timestamp");
                FilterDefinition<BsonDocument> filterParam = GetMongoFilter("Timestamp",timestampFromRedis!);

                //Read from mongoDb
                List<BsonDocument> documents = await eventsCollection.Find(filterParam).Sort(sortParam).ToListAsync();

                //If read collection isnt empty load it to REDIS and update latest timestamp in REDIS
                if(documents.Count > 0)
                {
                    
                    Dictionary<RedisKey, RedisValue> redisDict = CreateRedisDict(configReader,documents);
                    await LoadDictToRedisAsync(redisDict,redisDb);
                    SetLastTimestamp(configReader,documents, redisDb);
                }

                await Task.Delay(configReader.ETLWakeupTimer);
            } 
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    private static ConfigReader InitConfigReader()
    {
        string currentDirectory = Directory.GetCurrentDirectory();   
        string configFilePath = Path.Combine(currentDirectory,"..","config.yml");
        ConfigReader configReader = new ConfigReader(configFilePath);
        return configReader;
    }

    private static string SetLastTimestamp(ConfigReader configReader, List<BsonDocument> documents, IDatabase redisDb)
    {
        string lastTimestamp ="";

        //Get last timestamp from the sorted collection and parse it into a timestamp string with selected time format
        if (documents.Count > 0)
        {
            BsonDocument lastDocument = documents[documents.Count -1];
            lastTimestamp = lastDocument["Timestamp"].AsDateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); 
        }

        redisDb.StringSet("Last Timestamp", lastTimestamp);
        Console.WriteLine($"Last saved timestamp in REDIS: {redisDb.StringGet("Last Timestamp")}");

        return lastTimestamp;
    }

    //Returns an empty filter or a greater than filter if the value isnt null
    private static FilterDefinition<BsonDocument> GetMongoFilter(string filterKey, string filterValue)
    {  
        FilterDefinition<BsonDocument> filterParam = Builders<BsonDocument>.Filter.Empty;

        if (!string.IsNullOrEmpty(filterValue))
        {
            DateTime filterDate;
            if (DateTime.TryParse(filterValue, null, DateTimeStyles.RoundtripKind, out filterDate))
                filterParam = Builders<BsonDocument>.Filter.Gt(filterKey, filterDate);
        }

        return filterParam;
    }

    private static Dictionary<RedisKey, RedisValue> CreateRedisDict(ConfigReader configReader, List<BsonDocument> documents)
    {
        // Create a dictionary to hold all key-value pairs
        Dictionary<RedisKey, RedisValue> redisDict = new Dictionary<RedisKey, RedisValue>();

        // Loop through the mongo collection and prepare key-value pairs
        foreach (BsonDocument doc in documents)
        {
            Console.WriteLine($"Received from mongo: {doc}");

            string convertedTimestamp = doc["Timestamp"].AsDateTime.ToUniversalTime().ToString(configReader.TimeFormatKey);
            string redisKey = doc["ReporterId"].ToString() + ":" + convertedTimestamp;
            
            // Add the key-value pair to the dictionary
            redisDict[redisKey] = doc.ToJson();
        }

        return redisDict;
    }

    private static async Task LoadDictToRedisAsync(Dictionary<RedisKey, RedisValue> redisDict, IDatabase redisDb)
    {
        // Use MSET to set all key-value pairs at once
        if (redisDict.Count > 0)
        {
            await redisDb.StringSetAsync(redisDict.ToArray());
            Console.WriteLine($"Loaded into Redis: {redisDict.Count} new entries");
        }
    }
}



