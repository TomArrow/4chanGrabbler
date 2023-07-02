using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using Salaros.Configuration;

namespace _4chanGrabbler
{

    enum SearchType
    {
        Image,
        Text,
        ThreadText
    }


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();

            ConfigParserMod configParser = new ConfigParserMod("archiveSites.ini");

            archiveSites = new Dictionary<string, ArchiveSite>();

            foreach (ConfigSection section in configParser.Sections) {

                ArchiveSite siteHere = new ArchiveSite();

                siteHere.url = configParser.GetValue(section.SectionName,"url").ToLower().Trim();
                siteHere.boards = configParser.GetValue(section.SectionName, "boards").Trim().Split(",");
                for(int i = 0; i < siteHere.boards.Length; i++)
                {
                    siteHere.boards[i] = siteHere.boards[i].Trim();
                }
                siteHere.regex = new Regex(configParser.GetValue(section.SectionName, "regex").Trim(), RegexOptions.Multiline |RegexOptions.IgnoreCase | RegexOptions.Compiled);

                // URL replacements, to avoid redirects and whatnot sometimes.
                string[] urlReplaceRegexes = configParser.getDuplicateValueArray(section.SectionName, "imageUrlReplaceRegex");
                string[] urlReplaceResults = configParser.getDuplicateValueArray(section.SectionName, "imageUrlReplaceResult");

                if(urlReplaceRegexes.Length != urlReplaceResults.Length)
                {
                    MessageBox.Show($"Error. Section {section.SectionName} mismatch of the count of imageUrlReplaceRegex and imageUrlReplaceResult parameters. For each imageUrlReplaceRegex there must be one imageUrlReplaceResult.");
                } else
                {
                    siteHere.urlReplacements = new ReplacementRegex[urlReplaceRegexes.Length];
                    for (int i = 0; i < siteHere.urlReplacements.Length; i++)
                    {
                        siteHere.urlReplacements[i].regex = new Regex(urlReplaceRegexes[i].Trim(), RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        siteHere.urlReplacements[i].replacement = urlReplaceResults[i].Trim();
                    }
                }


                archiveSites.Add(siteHere.url, siteHere);
            }

            Directory.CreateDirectory("debuglogs");

        }

        Regex linkAnalyzer = new Regex("https?\\://((.*?)\\.)?(.*?)\\.(.*?)/(.*?)/thread/(\\d+?)(/?#[\\w\\d]+|/|$)", RegexOptions.Multiline |RegexOptions.IgnoreCase | RegexOptions.Compiled);
        Regex wildCardReplacer = new Regex(@"###([\w\d]+)###", RegexOptions.Multiline |RegexOptions.IgnoreCase | RegexOptions.Compiled);

        

        //Regex archiveHTMLAnalyzer = new Regex("https?\\://((.*?)\\.)?(.*?)\\.(.*?)/(.*?)/thread/(\\d+?)/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        //Regex fortuneHTMLAnalyzer = new Regex("https?\\://((.*?)\\.)?(.*?)\\.(.*?)/(.*?)/thread/(\\d+?)/", RegexOptions.IgnoreCase | RegexOptions.Compiled); // native 4chan

        struct ReplacementRegex
        {
            public Regex regex;
            public string replacement;
        }

        struct ArchiveSite
        {
            public string url;
            public string[] boards;
            public ReplacementRegex[] urlReplacements;
            public Regex regex;
        }
        Dictionary<string,ArchiveSite> archiveSites;

        const int maxStatusLength = 20000;
        const int retrycount = 10;
        const int retryTimeout = 10000;
        const int downloadThreads = 4;

        private string sanitizeFilename(string filename)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalid)
            {
                filename = filename.Replace(c.ToString(), "");
            }
            return filename;
        }

        private void updateStatus(string newLine)
        {
            lock (txtStatus)
            {
                string tmp = txtStatus.Text;
                tmp += Environment.NewLine + newLine;
                if (tmp.Length > maxStatusLength)
                {
                    tmp = tmp.Substring(tmp.Length - maxStatusLength);
                }
                txtStatus.Text = tmp;
                statusScrollViewer.ScrollToBottom();
            }
        }

        private void grabbleLink(string link)
        {
            MatchCollection matches = linkAnalyzer.Matches(link);
            foreach (Match match in matches)
            {
                string url = "";
                string domain="";
                string tld = "";
                string board = "";
                string threadnumber = "";
                bool success = true;
                try
                {
                    url = match.ToString();
                    domain = match.Groups[3].Value;
                    tld = match.Groups[4].Value;
                    board = match.Groups[5].Value;
                    threadnumber = match.Groups[6].Value;
                } catch(Exception e)
                {
                    // whatever
                    success = false;
                }

                if (success)
                {

                    bool isArchive = !(tld == "org" && (domain == "4chan" || domain == "4channel"));


                    string saveFolderPath = board+ Path.DirectorySeparatorChar + threadnumber+Path.DirectorySeparatorChar;
                    Directory.CreateDirectory(saveFolderPath);

                    var progressHandler = new Progress<string>(value =>
                    {
                        /*lock (txtStatus)
                        {
                            string tmp = txtStatus.Text;
                            tmp += Environment.NewLine +value.ToString();
                            if (tmp.Length > maxStatusLength)
                            {
                                tmp = tmp.Substring(tmp.Length - maxStatusLength);
                            }
                            txtStatus.Text = tmp;
                            statusScrollViewer.ScrollToBottom();
                        }*/
                        updateStatus(value.ToString());
                    });
                    var progress = progressHandler as IProgress<string>;

                    if (isArchive)
                    {
                        string urlHere = domain.ToLower() + "." + tld.ToLower();
                        ArchiveSite thisArchiveSite;
                        if (archiveSites.ContainsKey(urlHere))
                        {
                            thisArchiveSite = archiveSites[urlHere];
                        } else
                        {
                            progress.Report("Archive site info for "+urlHere+" could not be found. Add it in archiveSites.ini?");
                            continue;
                        }

                        _ = Task.Run(() =>
                        {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                            //var httpClient = new HttpClient();
                           
                            try
                            {
                                //httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));
                                //httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("deflate"));
                                //httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("br"));

                                //var response = await httpClient.GetAsync(url);/

                                //will throw an exception if not successful
                                //response.EnsureSuccessStatusCode();

                                //string content = await response.Content.ReadAsStringAsync();

                                // access req.Headers to get/set header values before calling GetResponse. 
                                // req.CookieContainer allows you access cookies.
                                //req.Timeout = 15000;
                                //req.Timeout = 1000 * oldTimeout; // This might be a bit excessive, will lead to pretty big timeout numbers sometimes maybe. Will have to see how it goes...
                                //req.ProtocolVersion = HttpVersion.Version10;
                                req.AutomaticDecompression = DecompressionMethods.All;
                                //req.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip, deflate, br");

                                req.Method = "GET";

                                var response = req.GetResponse();
                                string webcontent;
                                using (var strm = new StreamReader(response.GetResponseStream()))
                                {
                                    webcontent = strm.ReadToEnd();
                                    //webcontent = content;

                                    // These are the individual pictures
                                    MatchCollection htmlMatches = thisArchiveSite.regex.Matches(webcontent);

                                    ParallelOptions opt = new ParallelOptions();
                                    opt.MaxDegreeOfParallelism = downloadThreads;

                                    Parallel.ForEach<Match>(htmlMatches, opt,(Match htmlMatch) =>
                                    {
                                        bool retry = true;
                                        int currentTry = -1;
                                        while (retry && currentTry<=retrycount)
                                        {
                                            currentTry++;
                                            string imageUrl = "";
                                            string realName = "";
                                            try
                                            {

                                                imageUrl = htmlMatch.Groups["url"].Value;
                                                realName = htmlMatch.Groups["realname"].Value;

                                                foreach(ReplacementRegex replaceRegex in thisArchiveSite.urlReplacements)
                                                {
                                                    // First replace ###blah### wildcards in the replacement string using capture groups from the normal url finder regex
                                                    // Extra amount of flexibility that way.
                                                    string replacement = wildCardReplacer.Replace(replaceRegex.replacement, (Match m) =>
                                                    {
                                                        if (m.Groups.Count > 1 && m.Groups[1].Success && htmlMatch.Groups.ContainsKey(m.Groups[1].Value))
                                                        {
                                                            return htmlMatch.Groups[m.Groups[1].Value].Success ? htmlMatch.Groups[m.Groups[1].Value].Value : "";
                                                        }
                                                        return m.Value;
                                                    });
                                                    imageUrl = replaceRegex.regex.Replace(imageUrl, replacement);
                                                }

                                                string sanitizedRealName = sanitizeFilename(realName);

                                                using (var client = new WebClient())
                                                {
                                                    client.Headers.Set(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; rv:114.0) Gecko/20100101 Firefox/114.0");
                                                    bool shouldDownload = true;
                                                    FileInfo fileInfo;
                                                    if (File.Exists(saveFolderPath + sanitizedRealName))
                                                    {

                                                        fileInfo = new FileInfo(saveFolderPath + sanitizedRealName);
                                                        if (fileInfo.Length == 0)
                                                        {
                                                            progress.Report(saveFolderPath + sanitizedRealName+" is empty. Deleting and downloading.");
                                                            File.Delete(saveFolderPath + sanitizedRealName);
                                                        }
                                                        else
                                                        {
                                                            progress.Report(saveFolderPath + sanitizedRealName + " already exists. Skipping.");
                                                            shouldDownload = false;
                                                        }
                                                    }
                                                    if (shouldDownload)
                                                    {
                                                        client.DownloadFile(imageUrl, saveFolderPath + sanitizedRealName);
                                                        if (realName != sanitizedRealName)
                                                        {

                                                            progress.Report(imageUrl + " saved as " + sanitizedRealName + " (real name was " + realName + ")");
                                                        }
                                                        else
                                                        {

                                                            progress.Report(imageUrl + " saved as " + sanitizedRealName);
                                                        }
                                                    }
                                                }
                                                retry = false;

                                            }
                                            catch (Exception e)
                                            {
                                                progress.Report("Downloading '"+ imageUrl + "' ('"+ realName + "') failed. Error (try "+currentTry+" of "+retrycount+"): " + e.Message);
                                                if (e.InnerException != null)
                                                {

                                                    progress.Report(e.InnerException.Message);
                                                }
                                                System.Threading.Thread.Sleep(retryTimeout);
                                            }
                                        }
                                    });

                                }

                            }
                            catch (Exception e)
                            {
                                progress.Report("Error: " + e.Message);
                            }
                            
                        });

                    }
                    else
                    {
                        throw new NotImplementedException("Not yet implemented");
                    }
                }
            }

        }
        

        private void btnClipboardGrabble_Click(object sender, RoutedEventArgs e)
        {
            grabbleLink(txtClipboard.Text);
        }

        private void btnManualGrabble_Click(object sender, RoutedEventArgs e)
        {
            grabbleLink(txtManual.Text);
        }



        // This handling mostly also lifted from: https://stackoverflow.com/a/33018459
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Initialize the clipboard now that we have a window soruce to use
            var windowClipboardManager = new ClipboardManager(this);
            windowClipboardManager.ClipboardChanged += ClipboardChanged;
        }

        private void ClipboardChanged(object sender, EventArgs e)
        {
            // Handle your clipboard update here, debug logging example:
            if (Clipboard.ContainsText())
            {
                try
                {

                    txtClipboard.Text = Clipboard.GetText();
                }catch(Exception exe)
                {
                    // nothing. duh.
                    txtClipboard.Text = "-clipboard error-";
                }
            }
        }

        private void fourplebsImageSearchLinkCrawlButton_Click(object sender, RoutedEventArgs e)
        {
            if(fourplebsImageSearchTerm.Text.Trim() == "")
            {
                MessageBox.Show("Enter a search term first");
            } else
            {
                string searchTerm = fourplebsImageSearchTerm.Text.Trim();

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Select an output text file to append found links to";
                if(sfd.ShowDialog() == true)
                {
                    string saveFile = sfd.FileName;

                    string endDateResume = txtEndDateResume.Text.Trim();

                    fourPlebsSearch(SearchType.Image, searchTerm, null, saveFile,endDateResume);
                }
            }
        }

        private void pushShiftImageSearchLinkCrawlButton_Click(object sender, RoutedEventArgs e)
        {
            if (pushShiftImageSearchSubreddit.Text.Trim() == "")
            {
                MessageBox.Show("Enter a subreddit first");
            }
            else
            {
                string subreddit = pushShiftImageSearchSubreddit.Text.Trim();

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Select an output text file to append found links to";
                if (sfd.ShowDialog() == true)
                {
                    string saveFile = sfd.FileName;

                    string endDateResume = txtEndDateResume.Text.Trim();

                    pushShiftImageSearch(subreddit, saveFile, endDateResume);
                }
            }
        }

        // Subregex is for Text search and optional. Pass null to just return the whole post that was matched.
        private void fourPlebsSearch(SearchType searchType,string searchString, Regex subRegex, string fileToSaveTo, string endDateResume = "")
        {
            string searchTerm = HttpUtility.UrlEncode(searchString);

            JsonSerializerOptions opt = new JsonSerializerOptions();
            opt.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;



            var progressHandler = new Progress<string>(value =>
            {
                /*lock (txtStatus)
                {
                    string tmp = txtStatus.Text;
                    tmp += Environment.NewLine +value.ToString();
                    if (tmp.Length > maxStatusLength)
                    {
                        tmp = tmp.Substring(tmp.Length - maxStatusLength);
                    }
                    txtStatus.Text = tmp;
                    statusScrollViewer.ScrollToBottom();
                }*/
                updateStatus(value.ToString());
            });
            var progress = progressHandler as IProgress<string>;

            

            int requestsPerMinuteAllowed = 5;
            int timeoutBetweenRequests = (60000 / requestsPerMinuteAllowed);
            timeoutBetweenRequests += 1000; // just a buffer to be safe.

            string crawlUrlBase = null;
            if (searchType == SearchType.Image)
            {
                crawlUrlBase = "http://archive.4plebs.org/_/api/chan/search/?filename=" + searchTerm; // http://archive.4plebs.org/_/api/chan/search/?filename=wojak&end=2021-03-11
            } else if(searchType == SearchType.Text)
            {
                crawlUrlBase = "http://archive.4plebs.org/_/api/chan/search/?text=" + searchTerm; // http://archive.4plebs.org/_/api/chan/search/?filename=wojak&end=2021-03-11
            }

            if(crawlUrlBase == null)
            {
                MessageBox.Show("Invaalid crawl url base (invalid search type?)");
            }

            Regex zeroReplacer = new Regex(Regex.Escape("{\"0\":{"),RegexOptions.Compiled);
            Regex dateReformatter = new Regex(@"(\d+)/(\d+)/(\d+)\(\w+\)(\d+):(\d+)",RegexOptions.Singleline|RegexOptions.IgnoreCase|RegexOptions.Compiled);

            TimeZoneInfo fourchantimezone = TimeZoneInfo.CreateCustomTimeZone("4chan", new TimeSpan(-4, 00, 00), "4chan", "4chan");
            _ = Task.Run(() =>
            {

                bool searchFinished = false;
                bool isFirstRequest = true;
                string endDateToAppend = "";
                int index = 0;
                while (!searchFinished) {

                    string cookie = "";
                    Dispatcher.Invoke(()=> {
                        cookie = fourplebsCookies.Text;
                    });
                    cookie = cookie == null ? "" : cookie.Trim();

                    if(index > 0) System.Threading.Thread.Sleep(timeoutBetweenRequests);


                    string crawlUrl = crawlUrlBase;
                    string endDate = "";
                    if (!isFirstRequest)
                    {
                        endDate= endDateToAppend;
                    } else if(endDateResume != "")
                    {
                        endDate = endDateResume;
                    }
                    if(endDate != "")
                    {
                        crawlUrl += "&end=" + endDate;
                    }
                    progress.Report("try " + index++ + crawlUrl);
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(crawlUrl);
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; rv:114.0) Gecko/20100101 Firefox/114.0";

                    if(cookie.Length > 0)
                    {
                        req.Headers.Add(HttpRequestHeader.Cookie, cookie);
                    }

                    string webcontent = "";
                    try
                    {
                        req.AutomaticDecompression = DecompressionMethods.All;

                        req.Method = "GET";

                        var response = req.GetResponse();
                        using (var strm = new StreamReader(response.GetResponseStream()))
                        {
                            webcontent = strm.ReadToEnd();

                            File.WriteAllText(Helpers.GetUnusedFilename( "debuglogs/4plebs_" + searchString + "_" + endDate.Replace(':','-') + ".json"),webcontent);
                            //File.AppendAllText(fileToSaveTo, webcontent);

                            string newText = zeroReplacer.Replace(webcontent,"{\"root\":{", 1);

                            FourPlebsSearchResult.Rootobject ro = JsonSerializer.Deserialize<FourPlebsSearchResult.Rootobject>(newText, opt);
                            //RootobjectTweetDetail tweet = JsonSerializer.Deserialize<RootobjectTweetDetail>(file1, opt);
                            List<string> imageDlLinks = new List<string>();
                            Int64 lastTimeStamp = 0;
                            string lastFourChanDate = "";
                            foreach (FourPlebsSearchResult.Post post in ro.root.posts)
                            {
                                if(searchType == SearchType.Image)
                                {
                                    string dlLink = post.media.media_link;
                                    imageDlLinks.Add(dlLink);
                                } else if(searchType == SearchType.Text)
                                {
                                    string textMatch = post.comment;
                                    if (subRegex == null) {

                                        imageDlLinks.Add(textMatch);
                                    } else
                                    {
                                        // If regex specified, find matches within match
                                        MatchCollection mc = subRegex.Matches(textMatch);
                                        foreach(Match match in mc)
                                        {
                                            if(match.Groups.Count == 1) // No subgroups
                                            {
                                                imageDlLinks.Add(match.Groups[0].Value);
                                            } else if(match.Groups.Count > 1) // Otherwise each group one match
                                            {
                                                for(int g =1; g < match.Groups.Count; g++)
                                                {
                                                    imageDlLinks.Add(match.Groups[g].Value);
                                                }
                                            }
                                        }
                                    }
                                }
                                lastTimeStamp = post.timestamp;
                                lastFourChanDate = post.fourchan_date;
                            }

                            //DateTime actualDateTime = DateTimeOffset.FromUnixTimeSeconds(lastFourChanDate).UtcDateTime;
                            //endDateToAppend = DateTimeOffset.FromUnixTimeSeconds(lastFourChanDate).UtcDateTime.ToString("yyyy-MM-dd-HH-mm");

                            //DateTime cstTime = TimeZoneInfo.ConvertTimeFromUtc(timeUtc, cstZone);
                            //endDateToAppend = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(lastTimeStamp).UtcDateTime, fourchantimezone).ToString("yyyy-MM-dd-HH:mm");
                            endDateToAppend = dateReformatter.Replace(lastFourChanDate, "20$3-$1-$2-$4:$5");

                            //string allLinks = String.Join("\n", imageDlLinks);

                            File.AppendAllLines(fileToSaveTo, imageDlLinks);
                        }
                    }
                    catch (Exception e)
                    {
                        if (webcontent == "{\"error\":\"No results found.\"}")
                        {
                            progress.Report("Image search finished.");
                            searchFinished = true;
                        }
                        else
                        {
                            progress.Report(e.Message);
                            if (e.InnerException != null)
                            {

                                progress.Report(e.InnerException.Message);
                            }
                        }


                    }

                    isFirstRequest = false;
                }
                
                
            });
                
        }

        private void pushShiftImageSearch(string subreddit, string fileToSaveTo, string endDateResume = "")
        {
            string subredditText = HttpUtility.UrlEncode(subreddit);

            JsonSerializerOptions opt = new JsonSerializerOptions();
            opt.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;



            var progressHandler = new Progress<string>(value =>
            {
                /*lock (txtStatus)
                {
                    string tmp = txtStatus.Text;
                    tmp += Environment.NewLine +value.ToString();
                    if (tmp.Length > maxStatusLength)
                    {
                        tmp = tmp.Substring(tmp.Length - maxStatusLength);
                    }
                    txtStatus.Text = tmp;
                    statusScrollViewer.ScrollToBottom();
                }*/
                updateStatus(value.ToString());
            });
            var progress = progressHandler as IProgress<string>;

            

            int requestsPerMinuteAllowed = 200;
            int timeoutBetweenRequests = (60000 / requestsPerMinuteAllowed);
            //timeoutBetweenRequests += 500; // just a buffer to be safe.

            //string crawlUrlBase = "http://archive.4plebs.org/_/api/chan/search/?filename=" + searchTerm; // http://archive.4plebs.org/_/api/chan/search/?filename=wojak&end=2021-03-11
            string crawlUrlBase = "https://api.pushshift.io/reddit/search/submission/?subreddit=" + subredditText +"&sort=desc&sort_type=created_utc&size=100"; 

            //Regex zeroReplacer = new Regex(Regex.Escape("{\"0\":{"),RegexOptions.Compiled);
            //Regex dateReformatter = new Regex(@"(\d+)/(\d+)/(\d+)\(\w+\)(\d+):(\d+)",RegexOptions.Singleline|RegexOptions.IgnoreCase|RegexOptions.Compiled);

            //TimeZoneInfo fourchantimezone = TimeZoneInfo.CreateCustomTimeZone("4chan", new TimeSpan(-4, 00, 00), "4chan", "4chan");
            _ = Task.Run(() =>
            {

                bool searchFinished = false;
                bool isFirstRequest = true;
                string endDateToAppend = "";
                int index = 0;
                while (!searchFinished) {

                    System.Threading.Thread.Sleep(timeoutBetweenRequests);


                    string crawlUrl = crawlUrlBase;
                    string endDate = "";
                    if (!isFirstRequest)
                    {
                        endDate = endDateToAppend;
                    }
                    else if (endDateResume != "")
                    {
                        endDate = endDateResume;
                    }
                    if (endDate != "")
                    {
                        crawlUrl += "&before=" + endDate;
                    }
                    progress.Report("try " + index++ + crawlUrl);
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(crawlUrl);
                    //req.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:90.0) Gecko/20100101 Firefox/90.0";
                    string webcontent = "";
                    try
                    {
                        req.AutomaticDecompression = DecompressionMethods.All;

                        req.Method = "GET";

                        var response = req.GetResponse();
                        using (var strm = new StreamReader(response.GetResponseStream()))
                        {
                            webcontent = strm.ReadToEnd();

                            File.WriteAllText(Helpers.GetUnusedFilename("debuglogs/pushshift_" + subreddit + "_" + endDate + ".json"), webcontent);

                            PushShiftSearchResult.Rootobject ro = JsonSerializer.Deserialize<PushShiftSearchResult.Rootobject>(webcontent, opt);
                            List<string> imageDlLinks = new List<string>();
                            Int64 lastTimeStamp = 0;
                            if(ro.data.Length == 0)
                            {
                                progress.Report("Image search finished.");
                                searchFinished = true;
                            }
                            foreach (PushShiftSearchResult.Datum post in ro.data)
                            {
                                string dlLink = post.url;
                                imageDlLinks.Add(dlLink);
                                lastTimeStamp = post.created_utc;
                            }
                            if(endDateToAppend == lastTimeStamp.ToString()) // stuck in endless loop. no need to continue.
                            {
                                if(ro.data.Length < 100)
                                {

                                    progress.Report("Image search finished.");
                                    searchFinished = true;
                                } else
                                {
                                    // Too many posts sharing the same timestamp. Manually decrease timestamp.
                                    progress.Report("Image search reached loop. Manually decreasing timestamp. Likely will result in skipped posts.");
                                    endDateToAppend = (lastTimeStamp-1).ToString();
                                }
                            } else
                            {

                                endDateToAppend = lastTimeStamp.ToString();
                            }

                            File.AppendAllLines(fileToSaveTo, imageDlLinks);
                        }
                    }
                    catch (Exception e)
                    {
                        progress.Report(e.Message);
                        if (e.InnerException != null)
                        {

                            progress.Report(e.InnerException.Message);
                        }
                    }

                    isFirstRequest = false;
                }
                
                
            });
                
        }

        private void fourplebsTextSearchLinkCrawlButton_Click(object sender, RoutedEventArgs e)
        {
            if (fourplebsImageSearchTerm.Text.Trim() == "")
            {
                MessageBox.Show("Enter a search term first");
            }
            else
            {
                string searchTerm = fourplebsImageSearchTerm.Text.Trim();
                string subRegexText = fourplebsTextSearchRegex.Text.Trim();

                if(subRegexText.Length == 0)
                {
                    subRegexText = null;
                }

                Regex subRegex = null;

                if(subRegexText != null)
                {
                    try
                    {
                        subRegex = new Regex(subRegexText,RegexOptions.IgnoreCase  | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);
                    }
                    catch (Exception except)
                    {
                        MessageBox.Show("Regex compilation failed. \n"+except.Message);
                        return;
                    }
                }

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Select an output text file to append matches to";
                if (sfd.ShowDialog() == true)
                {
                    string saveFile = sfd.FileName;

                    string endDateResume = txtEndDateResume.Text.Trim();

                    fourPlebsSearch(SearchType.Text, searchTerm, subRegex, saveFile, endDateResume);
                }
            }
        }
    }
}
