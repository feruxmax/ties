using System.Collections.Generic;
using WebAppHosted.Client.Models;

namespace WebAppHosted.Client.Services
{
    public class VersionedData
    {
        public int Version { get; set; }
        public int FormatVersion { get; set; }
        public List<Notion> Data { get; set; }
    }
}