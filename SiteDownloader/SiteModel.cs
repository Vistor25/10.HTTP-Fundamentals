using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SiteDownloader
{
    public class SiteModel
    {
        public string Url { get; set; }
        public string FullUrl { get; set; }
        public string ParentUrl { get; set; }
        public int Level { get; set; }
        public string PathPage { get; set; }
        public string PathParentPage { get; set; }
    }
}
