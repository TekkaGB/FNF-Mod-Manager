﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;

namespace FNF_Mod_Manager
{
    /// <summary>
    /// Interaction logic for EditWindow.xaml
    /// </summary>
    public partial class FetchWindow : Window
    {
        public bool success;
        public Mod _mod;
        private Logger _logger;
        private string assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public FetchWindow(Mod mod, Logger logger)
        {
            InitializeComponent();
            _mod = mod;
            Title = $"Fetch Metadata for {_mod.name}";
            _logger = logger;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private Uri CreateUri(string url)
        {
            Uri uri;
            if ((Uri.TryCreate(url, UriKind.Absolute, out uri) || Uri.TryCreate("http://" + url, UriKind.Absolute, out uri)) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                // Use validated URI here
                string host = uri.DnsSafeHost;
                if (uri.Segments.Length != 3)
                    return null;
                switch (host)
                {
                    case "www.gamebanana.com":
                    case "gamebanana.com":
                        return uri;
                }
            }
            return null;
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Uri url = CreateUri(UrlBox.Text);
            if (url != null)
            {
                try
                {
                    var MOD_TYPE = char.ToUpper(url.Segments[1][0]) + url.Segments[1].Substring(1, url.Segments[1].Length - 3);
                    var MOD_ID = url.Segments[2];
                    var client = new HttpClient();
                    var requestUrl = $"https://gamebanana.com/apiv3/{MOD_TYPE}/{MOD_ID}";
                    string dataString = await client.GetStringAsync(requestUrl);
                    var data = JsonSerializer.Deserialize<GameBananaAPIV3>(dataString);

                    requestUrl = $"https://api.gamebanana.com/Core/Item/Data?itemtype={MOD_TYPE}&itemid={MOD_ID}&fields=" +
                        $"Preview().sStructuredDataFullsizeUrl(),Owner().name,description,Updates().bSubmissionHasUpdates()," +
                        $"Updates().aGetLatestUpdates(),RootCategory().name&return_keys=1";
                    string responseString = await client.GetStringAsync(requestUrl);
                    var response = JsonSerializer.Deserialize<GameBananaItem>(responseString);
                    var metadata = new Metadata();

                    metadata.submitter = response.Owner;
                    metadata.description = response.Description;
                    metadata.preview = response.EmbedImage;
                    metadata.homepage = url;
                    metadata.avi = data.Member.Avatar;
                    metadata.upic = data.Member.Upic;
                    metadata.cat = data.Category.Name;
                    metadata.caticon = data.Category.Icon;
                    metadata.section = data.Category.Model.Replace("Category", "");
                    if (metadata.section.Equals("Mod", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (metadata.cat.Equals(response.RootCat, StringComparison.InvariantCultureIgnoreCase))
                            metadata.section = "";
                        else
                            metadata.section = response.RootCat.Substring(0, response.RootCat.Length - 1);
                    }
                    if (response.HasUpdates != null && (bool)response.HasUpdates)
                        metadata.lastupdate = response.Updates[0].DateAdded;
                    else
                        metadata.lastupdate = new DateTime(1970, 1, 1);
                    string metadataString = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText($@"{assemblyLocation}/Mods/{_mod.name}/mod.json", metadataString);
                    success = true;
                    Close();
                }
                catch (Exception ex)
                {
                    _logger.WriteLine(ex.Message, LoggerType.Error);
                }
            }
            else
                _logger.WriteLine($"{UrlBox.Text} is invalid. The url should have the following format: https://gamebanana.com/<Mod Category>/<Mod ID>", LoggerType.Error);
        }
    }
}
