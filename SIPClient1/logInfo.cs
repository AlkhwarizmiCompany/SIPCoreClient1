using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIPClient1
{
    public class LogDocument
    {
        public string CallId { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string CallUri { get; set; }
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
    }

    public class LogEntry
    {
        public string Filename { get; set; }
        public string LogLevel { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

}
