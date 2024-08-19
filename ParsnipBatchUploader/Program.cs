using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using ParsnipData.Media;
using ParsnipData;
using System.Diagnostics;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using Image = ParsnipData.Media.Image;

namespace ParsnipBatchUploader
{
    internal class Program
    {
        private static readonly List<string> tagsAppSetting = ConfigurationManager.AppSettings["Tags"].Split(',').ToList();
        static void Main(string[] args)
        {
            var mediaTags = new List<MediaTag>();
            tagsAppSetting.ForEach(t => mediaTags.Add(new MediaTag(t.Trim())));
            var sourceDir = getResponse("Source");
            var destinationDir = getResponse("Destination");

            if (destinationDir.Substring(0, destinationDir.Length - 1) != "\\") destinationDir += '\\';

            

            

            var files = Directory.GetFiles(sourceDir);
            var total = files.Count();
            var current = 0;
            foreach(var file in files)
            {
                current++;
                Console.Write($"\r{current}/{total} - (Processing media...)                                                  ");
                var now = Parsnip.AdjustedTime;
                var originalExtension = $".{file.Split('.').Last()}";

                var originalsDir = $"{destinationDir}Originals";
                if (!Directory.Exists(originalsDir)) Directory.CreateDirectory(originalsDir);
                var mediaId = MediaId.NewMediaId();

                File.Copy(file, $"{destinationDir}Originals\\{mediaId}{originalExtension}");

                var media =
                    Image.IsValidFileExtension(originalExtension.Substring(1)) ? processImage() :
                    Video.IsValidFileExtension(originalExtension.Substring(1)) ? processVideo() :
                    throw new FileFormatException($"{originalExtension} is not supported at this time");

                foreach (var tag in mediaTags)
                {
                    var pair = new MediaTagPair(media, tag, 1);
                    pair.Insert();
                }

                Media processImage()
                {

                    var compressedDir = $"{destinationDir}Compressed";
                    if (!Directory.Exists(compressedDir)) Directory.CreateDirectory(compressedDir);

                    var placeholderDir = $"{destinationDir}Placeholders";
                    if (!Directory.Exists(placeholderDir)) Directory.CreateDirectory(placeholderDir);

                    var image = new Image()
                    {
                        Id = mediaId,
                        DateTimeCaptured = now,
                        DateTimeCreated = now,
                        CreatedById = 1,
                    };

                    Media.ProcessMediaThumbnail(image, image.Id.ToString(), originalExtension, destinationDir);

                    Console.Write($"\r{current}/{total} - (Uploading image - WARNING, YOU HAVE COMMENTED UPLOAD OF ORIGINAL...)                                                  ");
                    image.Upload(destinationDir, originalExtension);
                    Console.Write($"\r{current}/{total} - (Inserting metadata...)                                                  ");
                    image.Insert();

                    return image;
                }
                
                Media processVideo()
                {
                    var video = new Video()
                    {
                        Id = mediaId,
                        DateTimeCaptured = now,
                        DateTimeCreated = now,
                        CreatedById = 1,
                    };

                    video.Upload(destinationDir, originalExtension);
                    video.VideoData.OriginalFileDir = $"{video.VideoUploadsDir}Originals/{mediaId}{originalExtension}";
                    video.Insert();

                    return video;
                }
            }

            string getResponse(string prompt)
            {
                Console.Write($"{prompt}: ");
                var response = Console.ReadLine();
                if (!Directory.Exists(response)) return getResponse(prompt);

                return response;
            }
        }
    }

    public static class Extensions
    {
        private static readonly string FtpUrl = ConfigurationManager.AppSettings["FtpUrl"];
        private static readonly string Website = ConfigurationManager.AppSettings["WebsiteUrl"];
        private static readonly string RemoteImagesDir = ConfigurationManager.AppSettings["RemoteImagesDir"];
        private static readonly string RemoteVideosDir = ConfigurationManager.AppSettings["RemoteVideosDir"];
        private static readonly NetworkCredential FtpCredentials = new NetworkCredential(ConfigurationManager.AppSettings["FtpUsername"], ConfigurationManager.AppSettings["FtpPassword"]);
        public static void Upload(this ParsnipData.Media.Image image, string LocalThumbnailsDir, string originalExt)
        {
                //FtpUpload(image, LocalThumbnailsDir, RemoteImagesDir, "Originals", originalExt);
                FtpUpload(image, LocalThumbnailsDir, RemoteImagesDir, "Compressed", ".jpg");
                FtpUpload(image, LocalThumbnailsDir, RemoteImagesDir, "Placeholders", ".jpg");

            
        }

        public static void Upload(this Video video, string localVideosDir, string originalExt) =>
            FtpUpload(video, localVideosDir, RemoteVideosDir, "Originals", originalExt);

        private static void FtpUpload(Media media, string localDir, string remoteDir, string folder, string extension)
        {
            var ftpClient = (FtpWebRequest)WebRequest.Create($"{FtpUrl}/{Website}/wwwroot/{remoteDir}/{folder}/{media.Id}{extension}");
            ftpClient.Credentials = FtpCredentials;
            ftpClient.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
            ftpClient.UseBinary = true;
            ftpClient.KeepAlive = true;
            var fi = new FileInfo($"{localDir}\\{folder}\\{media.Id}{extension}");
            ftpClient.ContentLength = fi.Length;
            byte[] buffer = new byte[4097];
            int bytes = 0;
            int total_bytes = (int)fi.Length;
            try
            {
                using (FileStream fs = fi.OpenRead())
                {
                    using (Stream rs = ftpClient.GetRequestStream())
                    {
                        while (total_bytes > 0)
                        {
                            bytes = fs.Read(buffer, 0, buffer.Length);
                            rs.Write(buffer, 0, bytes);
                            total_bytes -= bytes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
