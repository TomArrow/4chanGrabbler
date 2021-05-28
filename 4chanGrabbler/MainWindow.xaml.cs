using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Salaros.Configuration;

namespace _4chanGrabbler
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();

            ConfigParser configParser = new ConfigParser("archiveSites.ini");

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

                archiveSites.Add(siteHere.url, siteHere);
            }

        }

        Regex linkAnalyzer = new Regex("https?\\://((.*?)\\.)?(.*?)\\.(.*?)/(.*?)/thread/(\\d+?)(/|$)", RegexOptions.Multiline |RegexOptions.IgnoreCase | RegexOptions.Compiled);

        

        //Regex archiveHTMLAnalyzer = new Regex("https?\\://((.*?)\\.)?(.*?)\\.(.*?)/(.*?)/thread/(\\d+?)/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        //Regex fortuneHTMLAnalyzer = new Regex("https?\\://((.*?)\\.)?(.*?)\\.(.*?)/(.*?)/thread/(\\d+?)/", RegexOptions.IgnoreCase | RegexOptions.Compiled); // native 4chan

        struct ArchiveSite
        {
            public string url;
            public string[] boards;
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
                        lock (txtStatus)
                        {
                            string tmp = txtStatus.Text;
                            tmp += Environment.NewLine +value.ToString();
                            if (tmp.Length > maxStatusLength)
                            {
                                tmp = tmp.Substring(tmp.Length - maxStatusLength);
                            }
                            txtStatus.Text = tmp;
                            statusScrollViewer.ScrollToBottom();
                        }
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
                                            try
                                            {

                                                string imageUrl = htmlMatch.Groups["url"].Value;
                                                string realName = htmlMatch.Groups["realname"].Value;

                                                string sanitizedRealName = sanitizeFilename(realName);

                                                using (var client = new WebClient())
                                                {
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
                                                progress.Report("Error (try "+currentTry+" of "+retrycount+"): " + e.Message);
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
                txtClipboard.Text = Clipboard.GetText();
            }
        }
    }
}
