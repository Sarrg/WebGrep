using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Windows.Threading;

namespace WebGrep
{
    public partial class Form1 : Form
    {
        static Regex currentSearchRegex;
        static Regex currentSiteRegex;
        static Regex urlRegex;
        static Regex hyperlinkRegex;
        static Regex hrefRegex;
        static Regex excludingFileTypes;
        static WebClient client;
        static int max_depth;
        static bool searching;

        static ConcurrentStack<string> downloadedUrls;
        static ConcurrentStack<string> invalidUrls;
        static ConcurrentStack<MatchCollection> matchList;
        static ConcurrentStack<string> linkList;

        static ConcurrentStack<Task> runningTasks;
        static CancellationTokenSource processLinksCTS;

        static WebBrowser browser;
        static string currentUrl;

        static bool validRegex;
        static bool validUrl;
        

        public Form1()
        {
            InitializeComponent();
            hyperlinkRegex = new Regex(@"(<a.*?>.*?</a>)", RegexOptions.Compiled | RegexOptions.Singleline);
            hrefRegex = new Regex(@"href=\""(.*?)\""", RegexOptions.Compiled | RegexOptions.Singleline);
            urlRegex = new Regex(@"^(http:\/\/www\.|https:\/\/www\.|http:\/\/|https:\/\/|www.)[a-z0-9]+([\-\.]{1}[a-z0-9]+)*\.[a-z]{2,5}(:[0-9]{1,5})?(\/\S*)?$", RegexOptions.Compiled | RegexOptions.Singleline);
            excludingFileTypes = new Regex(@".(?:avi|css|doc|exe|gif|jpeg|jpg|js|mid|midi|mp3|mpg|mpeg|mov|qt|pdf|png|ram|rar|tiff|wav|zip)", RegexOptions.Compiled | RegexOptions.Singleline);
            client = new WebClient();
            browser = new WebBrowser();

            max_depth = 4;
            downloadedUrls = new ConcurrentStack<string>();
            invalidUrls = new ConcurrentStack<string>();
            matchList = new ConcurrentStack<MatchCollection>();
            linkList = new ConcurrentStack<string>();
            runningTasks = new ConcurrentStack<Task>();

            validUrl = false;
            validRegex = false;
            searchButton.Enabled = false;
        }

        public static List<string> FindLinks(string file, string domain)
        {
            List<string> list = new List<string>();
            char[] tokens = {'#', '?'};

            MatchCollection m1 = hyperlinkRegex.Matches(file);

            foreach (Match m in m1)
            {
                string value = m.Groups[1].Value;

                Match m2 = hrefRegex.Match(value);
                if (m2.Success)
                {
                    string url = m2.Groups[1].Value;
                    if (!excludingFileTypes.IsMatch(url))
                    {
                        try
                        {
                            Uri subpage = new Uri(url.Split(tokens)[0]);
                            url = subpage.AbsoluteUri;
                            if (!list.Contains(url))
                                list.Add(url);
                        }
                        catch (System.UriFormatException ex)
                        {
                            if (url.StartsWith("/"))
                                url = url.Remove(0, 1);
                            if (!domain.EndsWith("/"))
                                url = "/" + url;
                            url = domain + url;
                            try
                            {
                                Uri subpage = new Uri(url.Split(tokens)[0]);
                                url = subpage.AbsoluteUri;
                                if (!list.Contains(url))
                                    list.Add(url);
                            }
                            catch (System.UriFormatException ex2)
                            { }
                        }
                    }
                }                
            }
            return list;
        }

        private Task ProcessLinksAsync()
        {
            processLinksCTS = new CancellationTokenSource();
            var task = Task.Run(() => 
            {
                bool allTaskFinished = false;
               
                while (!(linkList.IsEmpty && allTaskFinished) && searching)
                {
                    allTaskFinished = true;
                    string url = "";
                    bool success = linkList.TryPop(out url);
                    if (success && !downloadedUrls.Contains(url))
                    {
                        if (url.ToCharArray()[url.Length - 1] != '/')
                            url += "/";
                        downloadedUrls.Push(url);
                        Webcrawl(url);
                    }

                    foreach (Task t in runningTasks)
                    {
                        if (!t.IsCompleted)
                            allTaskFinished = false;
                    }
                }
            }, processLinksCTS.Token);
            return task;
        }

        public void Webcrawl(string url)
        {
            if (url.Split('/').Count() - 2 - currentUrl.Split('/').Count() < max_depth)
            {
                var task = Task.Run(() =>
                {
                    try
                    {
                        string html = client.DownloadString(url);
                        //downloadedUrls.Push(url);
                        Uri uri = new Uri(url);
                        var links = FindLinks(html, url);
                        foreach (string link in links)
                        {
                            if (!downloadedUrls.Contains(link) && link.IndexOf(currentUrl) >= 0)
                            {
                                linkList.Push(link);
                            }
                        }
                     
                        int matchCount = 0, matchLines = 0;
                        TreeNode node = new TreeNode();

                        string[] htmlLines = html.Split('\n');
                        for (int i = 0; i < htmlLines.Length; i++)
                        {
                            var matches = currentSearchRegex.Matches(htmlLines[i]);
                            if (matches.Count > 0)
                            {
                                matchCount += matches.Count;
                                matchLines++;
                                StringBuilder sb = new StringBuilder();
                                sb.Append(matches.Count);
                                if (matches.Count == 1)
                                    sb.Append(" match on Line ");
                                else sb.Append(" matches on Line ");
                                sb.Append(i);
                                TreeNode childNode = new TreeNode(sb.ToString());
                                childNode.Tag = matchList.Count;

                                for (int j = -2; j < 3; j++)
                                    childNode.Nodes.Add(new TreeNode("Line " + (i + j) + ": " + htmlLines[i + j]));

                                node.Nodes.Add(childNode);
                                matchList.Push(matches);
                            }
                        }

                        if (matchCount > 0)
                        {
                            string relativePath = url.Substring(url.IndexOf(urlTextBox.Text) + urlTextBox.Text.Length);
                            if (relativePath == "")
                                relativePath = "/";
                            StringBuilder sb = new StringBuilder();
                            sb.Append(relativePath).Append(" (").Append(matchCount);
                            if (matchCount == 1)
                                sb.Append(" match on line ").Append(node.Nodes[0].Text.Split(' ').Last()).Append(")");
                            else sb.Append(" matches on ").Append(matchLines).Append(" lines)");
                            node.Text = sb.ToString();

                            if (searching)
                                treeView1.BeginInvoke(new Action(() => { treeView1.Nodes.Add(node); treeView1.Sort(); }));
                        }
                    }

                    catch (Exception ex)
                    {
                        invalidUrls.Push(url);
                    }
                });
                runningTasks.Push(task);
            }
        }

        private void urlTextBox_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(urlTextBox.Text))
            {
                urlTextBox.BackColor = Color.White;
                validUrl = false;
            }
            else
            {
                if (urlRegex.IsMatch(urlTextBox.Text))
                {
                    urlTextBox.BackColor = Color.White;
                    validUrl = true;
                }
                else
                {
                    urlTextBox.BackColor = Color.Red;
                    validUrl = false;
                }
            }
            if (!validUrl || !validRegex)
            {
                searchButton.Enabled = false;
            }
            else searchButton.Enabled = true;
        }

        private void regexTextBox_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(regexTextBox.Text))
            {
                regexTextBox.BackColor = Color.White;
                validRegex = false;
            }
            else
            {
                
                try
                {
                    Regex.Match("", regexTextBox.Text);
                    regexTextBox.BackColor = Color.White;
                    validRegex = true;
                }
                catch (ArgumentException)
                {
                    regexTextBox.BackColor = Color.Red;
                    validRegex = false;
                }
            }

            if (!validUrl || !validRegex)
            {
                searchButton.Enabled = false;
            }
            else searchButton.Enabled = true;
        }

        private async void searchButton_Click(object sender, EventArgs e)
        {
            if (validRegex && validUrl)
            {
                if (!searching)
                {
                    searching = true;
                    runningTasks.Clear();
                    downloadedUrls.Clear();
                    invalidUrls.Clear();
                    matchList.Clear();
                    linkList.Clear();
                    treeView1.Nodes.Clear();
                    urlTextBox.Enabled = false;
                    regexTextBox.Enabled = false;
                    searchButton.Text = "Cancel";
                    
                    currentSearchRegex = new Regex(regexTextBox.Text, RegexOptions.Compiled | RegexOptions.Singleline);

                    currentUrl = urlTextBox.Text;
                    currentSiteRegex = new Regex(currentUrl, RegexOptions.Compiled | RegexOptions.Singleline);

                    if (!currentUrl.StartsWith("http"))
                        currentUrl = "https://" + currentUrl;
                    linkList.Push(currentUrl);

                    await ProcessLinksAsync();
                    
                    searching = false;
                    urlTextBox.Enabled = true;
                    regexTextBox.Enabled = true;
                    searchButton.Text = "Search";
                }
                else
                {
                    if(processLinksCTS != null)
                        processLinksCTS.Cancel();
                    searching = false;
                    linkList.Clear();
                }
            }
        }

        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            if(node != null && node.Parent != null && node.Parent.Parent == null)
            {
                int index = (int) node.Tag;
                MatchCollection m = matchList.ToArray()[index];
                WebbrowserForm web = new WebbrowserForm();
                Uri uri = new Uri(currentUrl + node.Parent.Text.Split(' ')[0]);
                web.SetWebpage(uri);
                web.SetMatches(m);
                web.Show();
            }
        }
    }
}
