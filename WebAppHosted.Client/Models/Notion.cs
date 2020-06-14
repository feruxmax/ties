using Newtonsoft.Json;

namespace WebAppHosted.Client.Models
{
    public class Notion
    {
        public string Title { get; set; } = null!; // todo: make immutable after .net5.0
    }
}