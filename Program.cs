using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.TypeConversion;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;

namespace TrackAcquire
{
    class Program
    {
        static void Main(string[] args)
        {
            // FeedID is confidential - do not hard code,  but read from user secrets
            var builder = new ConfigurationBuilder()
            .AddUserSecrets<Program>();

            var Configuration = builder.Build();
            var feedID = Configuration["FeedID"];

            Console.WriteLine("Hello! -- Looking for new SPOT messages - FeedID = {0}", feedID);

            var track = new SpotTrack(@"track.csv", feedID);

            if (track.currentTrack.Count > 0)
                Console.WriteLine(String.Format("Current track has {0} track points!", track.currentTrack.Count));
            else
                Console.WriteLine("No current track, or current track is empty");

            Console.WriteLine("Polling Spot feed for new messages...");
            var task = track.GetNewTrackPoints(true);
            var newTrackPoints = task.Result;
            Console.WriteLine(string.Format("Spot feed has {0} total messages", track.totalFeedMessageCount));

            if (newTrackPoints.Count > 0)
            {
                Console.WriteLine(String.Format("Found {0} new track points from Spot feed!", newTrackPoints.Count));
                foreach (var trackPoint in newTrackPoints)
                {
                    Console.WriteLine(String.Format("{0}, Lat {1}, Long {2}",
                        trackPoint.DateTime,
                        trackPoint.Latitude,
                        trackPoint.Longitude));
                }
                track.PersistNewTrackPoints(newTrackPoints);
                Console.WriteLine(String.Format("Persisted {0} new track points to track file!", newTrackPoints.Count));
            }
            else
                Console.WriteLine("Found no new track points from Spot feed!");
        }
    }
}

