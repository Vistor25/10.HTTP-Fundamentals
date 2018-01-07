using CsQuery;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SiteDownloader
{
    public class Downloader
    {
        public string Url { get; protected set; }
        public string Folder { get; protected set; }
        public int MaxHierarchyLevel { get; protected set; }

        private LevelMode mode = new LevelMode();

        private Queue<SiteModel> queueSites = new Queue<SiteModel>();

        private List<SiteModel> createdSites = new List<SiteModel>();

        private List<string> downloadedContent = new List<string>();

        private string fileExtensionFilter = "";

        private static Logger logger = LogManager.GetCurrentClassLogger();

        private SiteModel CurrentSite { get; set; }

        private int CountCreatedPage = 0;

        public Downloader(string url, string folder, int maxLevel)
        {
            if (String.IsNullOrEmpty(url) || String.IsNullOrEmpty(folder) || maxLevel < 0)
            {
                throw new ArgumentException($"Parameter url is not valid");
            }

            Url = url;
            Folder = folder;
            MaxHierarchyLevel = maxLevel;

            queueSites.Enqueue(new SiteModel { FullUrl = this.Url, Level = 0 });
        }

        public Downloader(string url, string folder, int maxLevel, string filter) : this(url, folder, maxLevel)
        {
            if (String.IsNullOrEmpty(filter))
            {
                throw new ArgumentException("Filter is null or empty");
            }

            fileExtensionFilter = filter;
        }

        public Downloader(string url, string folder, int maxLevel, string filter, LevelMode mode) : this(url, folder, maxLevel, filter)
        {
            this.mode = mode;
        }

        private void CheckUrl()
        {
            if (!CurrentSite.FullUrl.StartsWith("http"))
            {
                CurrentSite.Url = CurrentSite.FullUrl;
                if (CurrentSite.ParentUrl[CurrentSite.ParentUrl.Length - 1] == '/' && CurrentSite.FullUrl[0] == '/')
                {
                    CurrentSite.FullUrl = CurrentSite.ParentUrl + CurrentSite.FullUrl.Substring(1);
                }
                else
                {
                    CurrentSite.FullUrl = CurrentSite.ParentUrl + CurrentSite.FullUrl;
                }
            }
        }

        public async void GetPages()
        {
            while (queueSites.Count != 0)
            {
                HttpClient client = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage();
                CurrentSite = queueSites.Dequeue();
                CheckUrl();
                request.RequestUri = new Uri(CurrentSite.FullUrl);
                request.Method = HttpMethod.Get;

                logger.Info($"\"Get\" request started to {CurrentSite.FullUrl}");
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request);
                }
                catch (HttpRequestException ex)
                {
                    throw new HttpRequestException($"Error occured trying to reach {CurrentSite.FullUrl}", ex);
                }

                logger.Info($"\"Get\" request finished to {CurrentSite.FullUrl}");

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    HttpContent responseContent = response.Content;
                    var markup = await responseContent.ReadAsStringAsync();
                    if (mode == LevelMode.OnlyCurrentDomain && CurrentSite.Level != 0)
                    {
                        if (GetRootSite(CurrentSite.FullUrl) == GetRootSite(createdSites.FirstOrDefault(x => x.Level == 0).FullUrl))
                        {
                            CreateSite(markup);
                        }
                    }
                    else
                    {
                        CreateSite(markup);
                    }
                    if (CurrentSite.Level < MaxHierarchyLevel)
                    {
                        FindLinks(markup);
                    }
                    GetResources(markup);
                }
            }
        }

        public void GetResources(string html)
        {
            CQ cq = CQ.Create(html, HtmlParsingMode.Content, HtmlParsingOptions.AllowSelfClosingTags | HtmlParsingOptions.IgnoreComments);
            var links = cq["[src]:not([src='']):not(iframe):not([src^='//'])"].Select(item => item.GetAttribute("src"));

            foreach (var link in links)
            {
                if (!link.StartsWith("http"))
                {
                    if (CurrentSite.FullUrl[CurrentSite.FullUrl.Length - 1] == '/' && link[0] == '/')
                    {
                        DownloadFile(CurrentSite.FullUrl + link.Substring(1));
                    }
                    else
                    {
                        DownloadFile(CurrentSite.FullUrl + link);
                    }
                }
                DownloadFile(link);
            }
        }

        public async void DownloadFile(string urlFile)
        {
            byte[] data = null;
            bool access = true;

            if (urlFile.Contains('?'))
            {
                urlFile = urlFile.Split('?')[0];
            }

            string fileName = urlFile.Substring(urlFile.LastIndexOf('/') + 1);

            using (var client = new HttpClient())
            {
                if (fileName.Contains('.'))
                {
                    try
                    {
                        data = await client.GetByteArrayAsync(urlFile);
                    }
                    catch (InvalidOperationException ex)
                    {
                        logger.Info($"Cannot access to resource {urlFile}");
                        access = false;
                    }
                    catch (HttpRequestException ex)
                    {
                        logger.Info($"Resource not found {urlFile}");
                        access = false;
                    }
                }
            }

            if (fileName.Contains('.') && access)
            {
                var splitedName = fileName.Split('.');
                var fileExt = '.' + splitedName[splitedName.Length - 1];

                if (fileExtensionFilter.Contains(fileExt))
                {
                    logger.Info($"File {fileName} wasn't download because it's have extension that in filter");
                    return;
                }

                if (!downloadedContent.Contains(fileName))
                {
                    downloadedContent.Add(fileName);
                    using (var file = new FileStream(Path.Combine(Folder, fileName), FileMode.Create, FileAccess.Write))
                    {
                        await file.WriteAsync(data, 0, data.Length);
                    }
                }
            }
        }

        private void FindLinks(string html)
        {
            logger.Info($"Start process links on page {CurrentSite.FullUrl}");
            CQ cq = CQ.Create(html);
            foreach (IDomObject obj in cq.Find("a"))
            {
                if (obj.GetAttribute("href") != null)
                {
                    queueSites.Enqueue(new SiteModel { FullUrl = obj.GetAttribute("href"), ParentUrl = CurrentSite.FullUrl, Level = CurrentSite.Level + 1 });
                }
            }
        }
        private void CreateSite(string markup)
        {
            if (createdSites.FirstOrDefault(x => x.FullUrl == CurrentSite.FullUrl) == null)
            {
                logger.Info($"Save in file {CurrentSite.FullUrl}");
                string file = Folder + "/" + CountCreatedPage + ".html";
                using (FileStream fs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite))
                {
                    createdSites.Add(new SiteModel()
                    {
                        Url = CurrentSite.Url,
                        FullUrl = CurrentSite.FullUrl,
                        ParentUrl = CurrentSite.ParentUrl,
                        Level = CurrentSite.Level,
                        PathPage = file,
                        PathParentPage = createdSites.FirstOrDefault(x => x.FullUrl == CurrentSite.ParentUrl)?.PathPage
                    });

                    using (StreamWriter writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.WriteLine(markup);
                    }
                }

                if (!String.IsNullOrEmpty(CurrentSite.ParentUrl))
                {
                    ChangeRefferenceInContentSite(file, createdSites.FirstOrDefault(x => x.FullUrl == CurrentSite.ParentUrl)?.PathPage);
                }

                CountCreatedPage++;
            }
        }

        private string GetRootSite(string url)
        {
            return new Uri(url).Host;
        }

        private void ChangeRefferenceInContentSite(string pathFile, string pathChangeFile)
        {
            using (FileStream fs = new FileStream(pathChangeFile, FileMode.Open, FileAccess.ReadWrite))
            {
                string content;
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    fs.Position = 0;
                    content = sr.ReadToEnd();
                }

                if (!String.IsNullOrEmpty(CurrentSite.Url))
                {
                    content = content.Replace(CurrentSite.Url, pathFile);
                }
                else
                {
                    content = content.Replace(CurrentSite.FullUrl, pathFile);
                }

                using (StreamWriter sw = new StreamWriter(pathChangeFile, false))
                {
                    sw.Write(content);
                }
            }
        }


    }
}
