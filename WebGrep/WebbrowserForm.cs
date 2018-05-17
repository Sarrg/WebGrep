using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using mshtml;

namespace WebGrep
{
    public partial class WebbrowserForm : Form
    {
        private Uri uri;
        private MatchCollection matches;

        public WebbrowserForm()
        {
            InitializeComponent();
        }

        public void SetWebpage(Uri uri)
        {
            this.uri = uri;
        }

        public void SetMatches(MatchCollection m)
        {
            matches = m;
        }

        private void webBrowser1_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (matches != null)
            {
                foreach (Match match in matches)
                {
                    try
                    {
                        IHTMLTxtRange range = ((webBrowser1.Document.DomDocument as IHTMLDocument2).body as IHTMLBodyElement).createTextRange();
                        range.findText(match.Value, match.Index);
                        range.select();
                        range.pasteHTML("<span style='background-color: rgb(255, 255, 0);'>" + match.Value + "</span>");
                    }
                    catch (Exception ex)
                    { }
                }
            }
        }

        private void WebbrowserForm_Shown(object sender, EventArgs e)
        {
            webBrowser1.Navigate(uri);
        }
    }
}
