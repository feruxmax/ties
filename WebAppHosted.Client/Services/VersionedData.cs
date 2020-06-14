using System.Collections.Generic;
using WebAppHosted.Client.Models;

namespace WebAppHosted.Client.Services
{
    public class VersionedData
    {
        public int Version { get; set; }
        public int FormatVersion { get; } = 0;
        public List<Notion> Data { get; set; } = null!; // todo: make immutable after .net5.0
    }
}