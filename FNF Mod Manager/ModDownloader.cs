﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using SharpCompress.Common;
using System.Text.RegularExpressions;
using SharpCompress.Readers;
using FNF_Mod_Manager.UI;
using SharpCompress.Archives.SevenZip;
using System.Linq;
using SharpCompress.Archives;

namespace FNF_Mod_Manager
{
    public class ModDownloader
    {
        private string URL_TO_ARCHIVE;
        private string URL;
        private string DL_ID;
        private string MOD_TYPE;
        private string MOD_ID;
        private string fileName;
        private string assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        private bool cancelled;
        private HttpClient client = new HttpClient();
        private CancellationTokenSource cancellationToken = new CancellationTokenSource();
        private GameBananaItem response = new GameBananaItem();
        private GameBananaAPIV3 data = new GameBananaAPIV3();
        private ProgressBox progressBox;
        public async void BrowserDownload(GameBananaRecord record)
        {
            DownloadWindow downloadWindow = new DownloadWindow(record);
            downloadWindow.ShowDialog();
            if (downloadWindow.YesNo)
            {
                string downloadUrl = null;
                string fileName = null;
                if (record.Files.Count == 1)
                {
                    downloadUrl = record.Files[0].DownloadUrl;
                    fileName = record.Files[0].FileName;
                }
                else if (record.Files.Count > 1)
                {
                    UpdateFileBox fileBox = new UpdateFileBox(record.Files, record.Title);
                    fileBox.Activate();
                    fileBox.ShowDialog();
                    downloadUrl = fileBox.chosenFileUrl;
                    fileName = fileBox.chosenFileName;
                }
                if (downloadUrl != null && fileName != null)
                {
                    await DownloadFile(downloadUrl, fileName, new Progress<DownloadProgress>(ReportUpdateProgress),
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken.Token));
                    if (!cancelled)
                        await ExtractFile(fileName, record);
                }
            }
        }
        public async void Download(string line, bool running)
        {
            if (ParseProtocol(line))
            {
                if (await GetData())
                {
                    DownloadWindow downloadWindow = new DownloadWindow(response);
                    downloadWindow.ShowDialog();
                    if (downloadWindow.YesNo)
                    {
                        await DownloadFile(URL_TO_ARCHIVE, fileName, new Progress<DownloadProgress>(ReportUpdateProgress),
                            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken.Token));
                        if (!cancelled)
                            await ExtractFile(fileName);
                    }
                }
            }
            if (running)
                Environment.Exit(0);
        }

            private async Task<bool> GetData()
        {
            try
            {
                string responseString = await client.GetStringAsync(URL);
                response = JsonSerializer.Deserialize<GameBananaItem>(responseString);
                fileName = response.Files[DL_ID].FileName;
                string dataString = await client.GetStringAsync($"https://gamebanana.com/apiv3/{MOD_TYPE}/{MOD_ID}");
                data = JsonSerializer.Deserialize<GameBananaAPIV3>(dataString);
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show($"Error while fetching data: {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        private void ReportUpdateProgress(DownloadProgress progress)
        {
            if (progress.Percentage == 1)
            {
                progressBox.finished = true;
            }
            progressBox.progressBar.Value = progress.Percentage * 100;
            progressBox.taskBarItem.ProgressValue = progress.Percentage;
            progressBox.progressTitle.Text = $"Downloading {progress.FileName}...";
            progressBox.progressText.Text = $"{Math.Round(progress.Percentage * 100, 2)}% " +
                $"({StringConverters.FormatSize(progress.DownloadedBytes)} of {StringConverters.FormatSize(progress.TotalBytes)})";
        }

        private bool ParseProtocol(string line)
        {
            try
            {
                line = line.Replace("filedaddy:", "");
                string[] data = line.Split(',');
                URL_TO_ARCHIVE = data[0];
                // Used to grab file info from dictionary
                var match = Regex.Match(URL_TO_ARCHIVE, @"\d*$");
                DL_ID = match.Value;
                MOD_TYPE = data[1];
                MOD_ID = data[2];
                URL = $"https://api.gamebanana.com/Core/Item/Data?itemtype={MOD_TYPE}&itemid={MOD_ID}&fields=name,Files().aFiles(),Preview().sStructuredDataFullsizeUrl()," +
                    $"Preview().sSubFeedImageUrl(),Owner().name,description,Updates().bSubmissionHasUpdates()," +
                        $"Updates().aGetLatestUpdates(),RootCategory().name&return_keys=1";
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show($"Error while parsing {line}: {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        private async Task ExtractFile(string fileName, GameBananaRecord record)
        {
            await Task.Run(() =>
            {
                string _ArchiveSource = $@"{assemblyLocation}/Downloads/{fileName}";
                string _ArchiveType = Path.GetExtension(fileName);
                string ArchiveDestination = $@"{assemblyLocation}/Mods/{string.Concat(record.Title.Split(Path.GetInvalidFileNameChars()))}";
                // Find a unique destination if it already exists
                var counter = 2;
                while (Directory.Exists(ArchiveDestination))
                {
                    ArchiveDestination = $@"{assemblyLocation}/Mods/{string.Concat(record.Title.Split(Path.GetInvalidFileNameChars()))} ({counter})";
                    ++counter;
                }
                if (File.Exists(_ArchiveSource))
                {
                    try
                    {
                        if (Path.GetExtension(_ArchiveSource).Equals(".7z", StringComparison.InvariantCultureIgnoreCase))
                        {
                            using (var archive = SevenZipArchive.Open(_ArchiveSource))
                            {
                                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                                {
                                    entry.WriteToDirectory(ArchiveDestination, new ExtractionOptions()
                                    {
                                        ExtractFullPath = true,
                                        Overwrite = true
                                    });
                                }
                            }
                        }
                        else
                        {
                            using (Stream stream = File.OpenRead(_ArchiveSource))
                            using (var reader = ReaderFactory.Open(stream))
                            {
                                while (reader.MoveToNextEntry())
                                {
                                    if (!reader.Entry.IsDirectory)
                                    {
                                        reader.WriteEntryToDirectory(ArchiveDestination, new ExtractionOptions()
                                        {
                                            ExtractFullPath = true,
                                            Overwrite = true
                                        });
                                    }
                                }
                            }
                        }
                        if (!File.Exists($@"{ArchiveDestination}/mod.json"))
                        {
                            Metadata metadata = new Metadata();
                            metadata.submitter = record.Owner.Name;
                            metadata.description = record.Description;
                            metadata.preview = record.Image;
                            metadata.homepage = record.Link;
                            metadata.avi = record.Owner.Avatar;
                            metadata.upic = record.Owner.Upic;
                            metadata.cat = record.CategoryName;
                            metadata.caticon = record.Category.Icon;
                            metadata.lastupdate = record.DateUpdated;
                            string metadataString = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText($@"{ArchiveDestination}/mod.json", metadataString);
                        }
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show($"Couldn't extract {fileName}: {e.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                // Check if folder output folder exists, if not nothing was extracted
                if (!Directory.Exists(ArchiveDestination))
                {
                    MessageBox.Show($"Didn't extract {fileName} due to improper format", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    // Only delete if successfully extracted
                    File.Delete(_ArchiveSource);
                }
            });

        }
        // Extract download to Mods directory
        private async Task ExtractFile(string fileName)
        {
            await Task.Run(() =>
            {
                string _ArchiveSource = $@"{assemblyLocation}/Downloads/{fileName}";
                string _ArchiveType = Path.GetExtension(fileName);
                string ArchiveDestination = $@"{assemblyLocation}/Mods/{string.Concat(response.Name.Split(Path.GetInvalidFileNameChars()))}";
                // Find a unique destination if it already exists
                var counter = 2;
                while (Directory.Exists(ArchiveDestination))
                {
                    ArchiveDestination = $@"{assemblyLocation}/Mods/{response.Name} ({counter})";
                    ++counter;
                }
                if (File.Exists(_ArchiveSource))
                {
                    try
                    {
                        if (Path.GetExtension(_ArchiveSource).Equals(".7z", StringComparison.InvariantCultureIgnoreCase))
                        {
                            using (var archive = SevenZipArchive.Open(_ArchiveSource))
                            {
                                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                                {
                                    entry.WriteToDirectory(ArchiveDestination, new ExtractionOptions()
                                    {
                                        ExtractFullPath = true,
                                        Overwrite = true
                                    });
                                }
                            }
                        }
                        else
                        {
                            using (Stream stream = File.OpenRead(_ArchiveSource))
                            using (var reader = ReaderFactory.Open(stream))
                            {
                                while (reader.MoveToNextEntry())
                                {
                                    if (!reader.Entry.IsDirectory)
                                    {
                                        Console.WriteLine(reader.Entry.Key);
                                        reader.WriteEntryToDirectory(ArchiveDestination, new ExtractionOptions()
                                        {
                                            ExtractFullPath = true,
                                            Overwrite = true
                                        });
                                    }
                                }
                            }
                        }
                        if (!File.Exists($@"{ArchiveDestination}/mod.json"))
                        {
                            Metadata metadata = new Metadata();
                            metadata.submitter = response.Owner;
                            metadata.description = response.Description;
                            metadata.preview = response.EmbedImage;
                            metadata.homepage = new Uri($"https://gamebanana.com/{MOD_TYPE.ToLower()}s/{MOD_ID}");
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
                            File.WriteAllText($@"{ArchiveDestination}/mod.json", metadataString);
                        }
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show($"Couldn't extract {fileName}: {e.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                // Check if folder output folder exists, if not nothing was extracted
                if (!Directory.Exists(ArchiveDestination))
                {
                    MessageBox.Show($"Didn't extract {fileName} due to improper format", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    // Only delete if successfully extracted
                    File.Delete(_ArchiveSource);
                }
            });
            
        }
        private async Task DownloadFile(string uri, string fileName, Progress<DownloadProgress> progress, CancellationTokenSource cancellationToken)
        {
            try
            {
                // Create the downloads folder if necessary
                Directory.CreateDirectory($@"{assemblyLocation}/Downloads");
                // Download the file if it doesn't already exist
                if (File.Exists($@"{assemblyLocation}/Downloads/{fileName}"))
                {
                    try
                    {
                        File.Delete($@"{assemblyLocation}/Downloads/{fileName}");
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show($"Couldn't delete the already existing {assemblyLocation}/Downloads/{fileName} ({e.Message})", 
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                progressBox = new ProgressBox(cancellationToken);
                progressBox.progressBar.Value = 0;
                progressBox.finished = false;
                progressBox.Title = $"Download Progress";
                progressBox.Show();
                progressBox.Activate();
                // Write and download the file
                using (var fs = new FileStream(
                    $@"{assemblyLocation}/Downloads/{fileName}", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await client.DownloadAsync(uri, fs, fileName, progress, cancellationToken.Token);
                }
                progressBox.Close();
            }
            catch (OperationCanceledException)
            {
                // Remove the file is it will be a partially downloaded one and close up
                File.Delete($@"{assemblyLocation}/Downloads/{fileName}");
                if (progressBox != null)
                {
                    progressBox.finished = true;
                    progressBox.Close();
                    cancelled = true;
                }
                return;
            }
            catch (Exception e)
            {
                if (progressBox != null)
                {
                    progressBox.finished = true;
                    progressBox.Close();
                }
                MessageBox.Show($"Error whilst downloading {fileName}: {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                cancelled = true;
            }
        }

    }
}
