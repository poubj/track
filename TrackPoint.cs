using System;

namespace TrackAcquire
{
    public class TrackPoint
    {
        public string Acquired { get; set; }
        public string MessageType { get; set; }
        public DateTime DateTime { get; set; }
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public float Altitude { get; set; }
        public float Heading { get; set; }
        public float Speed { get; set; }
        public float Log { get; set; }
        public float COG { get; set; }
        public float SOG { get; set; }
        public float Depth { get; set; }
        public float WindSpeed { get; set; }
        public float WindDirection { get; set; }
        public float MotorRPM { get; set; }
        public float MotorHours { get; set; }
        public float Barometer { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public string SourceData { get; set; }
    }
}