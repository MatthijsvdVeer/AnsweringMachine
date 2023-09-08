using System.Collections.Generic;
using Newtonsoft.Json;

namespace SearchThingy;

public class Thingy
{

        [JsonProperty("@search.score")]
        public double searchscore { get; set; }
        public string content { get; set; }
        public string filepath { get; set; }
        public string title { get; set; }
        public string url { get; set; }
        public string id { get; set; }
        public string chunk_id { get; set; }
        public string last_updated { get; set; }
        public List<double> contentVector { get; set; }
    
}