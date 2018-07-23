using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.TypeConversion;
using CsvHelper.Configuration;

namespace TrackAcquire
{
    // JSON data from SPOT API feed (https://api.findmespot.com/spot-main-web/consumer/rest-api/2.0/public/feed/FEED_ID_HERE/message.json)
    [DataContract]
    public class SpotJSON
    {
        [DataMember(Name = "response")]
        public SpotResponse Response { get; protected set; }
    }

    [DataContract]
    public class SpotResponse
    {
        [DataMember(Name = "feedMessageResponse")]
        public SpotFeedMessageResponse FeedMessageResponse { get; protected set; }
    }

    [DataContract]
    public class SpotFeedMessageResponse
    {
        [DataMember(Name = "count")]
        public int Count { get; set; }

        [DataMember(Name = "totalCount")]
        public int TotalCount { get; set; }

        [DataMember(Name = "messages")]
        public SpotMessagesContainer SpotMessagesContainer { get; set; }
    }

    [DataContract]
    public class SpotMessagesContainer
    {
        [DataMember(Name = "message")]
        public List<SpotMessage> SpotMessages { get; set; }
    }

    [DataContract]
    public class SpotMessage
    {
        [DataMember(Name = "id")]
        public Int32 Id { get; set; }

        [DataMember(Name = "messengerId")]
        public string MessengerId { get; set; }

        [DataMember(Name = "messengerName")]
        public string MessengerName { get; set; }

        [DataMember(Name = "modelId")]
        public string ModelId { get; set; }

        [DataMember(Name = "messageType")]
        public string MessageType { get; set; }

        [DataMember(Name = "messageContent")]
        public string MessageContent { get; set; }

        [DataMember(Name = "batteryState")]
        public string BatteryState { get; set; }

        [DataMember(Name = "dateTime")]
        public string SpotDateTime { get; set; }

        [DataMember(Name = "unixTime")]
        public Int32 UnixTime { get; set; }

        [DataMember(Name = "latitude")]
        public float Latitude { get; set; }

        [DataMember(Name = "longitude")]
        public float Longitude { get; set; }

        [DataMember(Name = "altitude")]
        public float Altitude { get; set; }

    }

    [DataContract]
    public class SpotSpecificData
    {
        [DataMember(Name = "id")]
        public Int32 Id { get; set; }

        [DataMember(Name = "messengerId")]
        public string MessengerId { get; set; }

        [DataMember(Name = "messengerName")]
        public string MessengerName { get; set; }

        [DataMember(Name = "modelId")]
        public string ModelId { get; set; }

        [DataMember(Name = "messageContent")]
        public string MessageContent { get; set; }

        [DataMember(Name = "batteryState")]
        public string BatteryState { get; set; }
    }

    public class SpotTrack
    {
        public SpotTrack(string csvFileName, string feedID)
        {
            this.csvFileName = csvFileName;
            this.feedID = feedID;
            currentTrack = new List<TrackPoint>();

            // Load existing track from storage
            if (File.Exists(csvFileName))
            {
                var sr = new StreamReader(csvFileName);
                var csvReader = new CsvReader(sr);

                var records = csvReader.GetRecords<TrackPoint>();

                foreach (var record in records)
                    currentTrack.Add(record);

                sr.Close();
            }
        }

        public static string SerializeJSon<T>(T t)
        {
            MemoryStream stream = new MemoryStream();
            DataContractJsonSerializer ds = new DataContractJsonSerializer(typeof(T));
            DataContractJsonSerializerSettings s = new DataContractJsonSerializerSettings();
            ds.WriteObject(stream, t);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            stream.Close();
            return jsonString;
        }

        private static readonly HttpClient HttpClient;

        static SpotTrack()
        {
            HttpClient = new HttpClient();
        }

        private List<TrackPoint> FilterTrackPointsFromMessages(List<SpotMessage> spotMessages, DateTime acquired)
        {
            TrackPoint lastTrackPoint = null;
            if (currentTrack.Count > 0)
                lastTrackPoint = currentTrack[currentTrack.Count - 1];

            var newTrackPoints = new List<TrackPoint>();

            foreach (var message in spotMessages)
            {
                var spotMessageDateTime = DateTimeOffset.FromUnixTimeSeconds(message.UnixTime);

                // If message is older than current track, then discard and stop
                if ((lastTrackPoint != null) && (spotMessageDateTime.DateTime <= lastTrackPoint.DateTime))
                    break;

                // Since message is newer, add it to the track
                var serializer = new DataContractJsonSerializer(typeof(SpotSpecificData));
                var spotSpecificData = new SpotSpecificData
                {
                    Id = message.Id,
                    MessengerId = message.MessengerId,
                    MessengerName = message.MessengerName,
                    ModelId = message.ModelId,
                    MessageContent = message.MessageContent,
                    BatteryState = message.BatteryState
                };

                var jsonSpotSpecificData = SerializeJSon(spotSpecificData);

                var acquiredString = string.Format("{0,0:D4}-{1,0:D2}-{2,0:D2}T{3,0:D2}:{4,0:D2}:{5,0:D2}.{6,0:D3}",
                    acquired.Year,
                    acquired.Month,
                    acquired.Day,
                    acquired.Hour,
                    acquired.Minute,
                    acquired.Second,
                    acquired.Millisecond
                    );

                var trackPoint = new TrackPoint
                {
                    Acquired = acquiredString,
                    MessageType = message.MessageType,
                    DateTime = DateTimeOffset.FromUnixTimeSeconds(message.UnixTime).DateTime,
                    Latitude = message.Latitude,
                    Longitude = message.Longitude,
                    Altitude = message.Altitude,
                    Heading = 0,
                    Speed = 0,
                    Log = 0,
                    COG = 0,
                    SOG = 0,
                    Depth = 0,
                    WindSpeed = 0,
                    WindDirection = 0,
                    MotorRPM = 0,
                    MotorHours = 0,
                    Barometer = 0,
                    Message = null,
                    Source = "SPOT",
                    SourceData = jsonSpotSpecificData
                };
                newTrackPoints.Add(trackPoint);
            }
            return newTrackPoints;
        }

        public async Task<List<TrackPoint>> GetNewTrackPoints(bool saveJsonReponses)
        {
            var serializer = new DataContractJsonSerializer(typeof(SpotJSON));

            var newTrackPoints = new List<TrackPoint>();
            int startMessageNumber = 0;

            SpotJSON spotJSON;
            do
            {
                var url = string.Format(urlRaw, feedID) + string.Format("?start={0}", startMessageNumber);
                var stream = await HttpClient.GetStreamAsync(url);

                var utcNow = DateTime.UtcNow;
                var filePrefix = string.Format("{0,0:D4}{1,0:D2}{2,0:D2}-{3,0:D2}{4,0:D2}{5,0:D2}-{6,0:D3}",
                    utcNow.Year,
                    utcNow.Month,
                    utcNow.Day,
                    utcNow.Hour,
                    utcNow.Minute,
                    utcNow.Second,
                    utcNow.Millisecond
                    );

                if (saveJsonReponses == true)
                {
                    var fileName = string.Format("{0}-{1}.json", filePrefix, startMessageNumber);

                    var fs = new FileStream(fileName, FileMode.Create);
                    stream.CopyTo(fs);
                    fs.Flush();

                    fs.Seek(0, 0);
                    spotJSON = serializer.ReadObject(fs) as SpotJSON;
                    fs.Close();
                }
                else
                    spotJSON = serializer.ReadObject(stream) as SpotJSON;

                var messages = spotJSON.Response.FeedMessageResponse.SpotMessagesContainer.SpotMessages;
                totalFeedMessageCount = spotJSON.Response.FeedMessageResponse.TotalCount;

                var newTrackPointsFromThisPage = FilterTrackPointsFromMessages(messages, utcNow);
                newTrackPoints.AddRange(newTrackPointsFromThisPage);

                if (newTrackPointsFromThisPage.Count < messages.Count)
                    break;

                startMessageNumber += messages.Count;

            } while (startMessageNumber < totalFeedMessageCount);

            newTrackPoints.Reverse();

            return newTrackPoints;
        }

        public void PersistNewTrackPoints(List<TrackPoint> newTrackPoints)
        {
            var trackFileExists = File.Exists(csvFileName);

            var sw = new StreamWriter(csvFileName, append: true);
            var csvWriter = new CsvWriter(sw);

            // DateTime must be formatted with "s" e.g. 2018-06-11T03:36:01
            var options = new TypeConverterOptions();
            string[] f = { "s" };
            options.Formats = f;

            csvWriter.Configuration.TypeConverterOptionsCache.AddOptions<DateTime>(options);

            if (!trackFileExists)
            {
                csvWriter.WriteHeader<TrackPoint>();
                csvWriter.NextRecord();
            }

            foreach (var trackPoint in newTrackPoints)
            {
                csvWriter.WriteRecord(trackPoint);
                csvWriter.NextRecord();
            }
            sw.Close();
        }

        public List<TrackPoint> currentTrack { get; }
        private string feedID;
        private string csvFileName;
        private static string urlRaw = "https://api.findmespot.com/spot-main-web/consumer/rest-api/2.0/public/feed/{0}/message.json";
        public int totalFeedMessageCount { get; set; }

        public async Task<List<SpotMessage>> GetMessagesFromHTTP(HttpClient client, string url)
        {
            //client.DefaultRequestHeaders.Accept.Clear();
            //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            //client.DefaultRequestHeaders.Add("User-Agent", ".NET Foundation Repository Reporter");

            var task = client.GetStreamAsync(url);

            var serializer = new DataContractJsonSerializer(typeof(SpotJSON));
            var spotJSON = serializer.ReadObject(await task) as SpotJSON;

            return spotJSON.Response.FeedMessageResponse.SpotMessagesContainer.SpotMessages;
        }

        public static List<SpotMessage> GetMessagesFromFile(string FileName)
        {
            var serializer = new DataContractJsonSerializer(typeof(SpotJSON));

            FileStream fs = File.OpenRead(FileName);
            var spotJSON = serializer.ReadObject(fs) as SpotJSON;
            fs.Close();

            spotJSON.Response.FeedMessageResponse.SpotMessagesContainer.SpotMessages.Reverse();
            return spotJSON.Response.FeedMessageResponse.SpotMessagesContainer.SpotMessages;
        }
    }
}
