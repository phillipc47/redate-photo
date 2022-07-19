namespace redate_photo
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using ExifLib;
    using redate_photo.Models;

    public class Program
    {
        public static void Main(string[] arguments)
        {
            if (arguments.Length != 1)
            {
                Console.WriteLine($"Arguments: ");
                Console.WriteLine($"1: Directory to process");
                return;
            }

            var directoryToProcess = arguments[0];
            if (!Directory.Exists(directoryToProcess))
            {
                Console.WriteLine($"The specified directory {directoryToProcess} does not exist");
                return;
            }

            Console.WriteLine($"Processing directory {directoryToProcess}");

            var unprocessedFiles = new List<string>();

            // Sometimes there are files with the same name, just different extensions
            var allFiles = LookupFiles(directoryToProcess);
            AvoidCollisionsByRenamingFiles(allFiles);

            // Refresh the file list because you just potentially renamed some files
            allFiles = LookupFiles(directoryToProcess);

            // Change the date of each photo to be the original photo taken date
            foreach (var file in allFiles)
            {
                var photoData = BuildPhotoData(file);

                if (!photoData.CanBeProcessed)
                {
                    unprocessedFiles.Add(file);
                    continue;
                }

                ChangeFileDate(photoData);
            }

            // Move unprocessed files to their own directory
            MoveFiles(unprocessedFiles, directoryToProcess, "Not Processed");

            // Group files into folders by date
                allFiles = LookupFiles(directoryToProcess);

            if (allFiles.Count > 0)
            {
                // Build a table, keyed by date, containing a list of file names
                var dateFileTable = new Dictionary<DateTime, IList<string>>();
                foreach (var file in allFiles)
                {
                    var candidateDate = File.GetCreationTime(file).Date;

                    if (!dateFileTable.ContainsKey(candidateDate))
                    {
                        dateFileTable.Add(candidateDate, new List<string>());
                    }

                    dateFileTable[candidateDate].Add(file);
                }

                // Do we have any categorizd files?
                if (dateFileTable.Count > 0)
                {
                    // Create a parent level directory
                    var organizedDirectory = Directory.CreateDirectory(Path.Combine(directoryToProcess, "Organized"));

                    // Process each date
                    foreach (var dateKey in dateFileTable.Keys)
                    {
                        // Create a directory of the form YYYY MM DD
                        var directoryToCreate = Path.Combine(organizedDirectory.FullName, dateKey.Date.ToString("yyyy MM dd"));
                        var destinationDirectory = Directory.CreateDirectory(directoryToCreate);

                        // Grab each file for this date
                        var currentDateFiles = dateFileTable[dateKey];

                        // Copy them from the original location into the new organized location
                        foreach (var file in currentDateFiles)
                        {
                            // Move the file
                            var destinationFile = Path.Combine(destinationDirectory.FullName, Path.GetFileName(file));
                            File.Move(file, destinationFile);

                            // Move any associated files as well
                            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                            var directoryToSearch = Path.GetDirectoryName(file);
                            if (string.IsNullOrEmpty(directoryToSearch))
                            {
                                continue;
                            }

                            // Are there any related files?  Sometimes a jpg will have the association MOV
                            var relatedFiles = Directory.GetFiles(directoryToSearch, $"*{fileNameWithoutExtension}*");
                            if (relatedFiles.Length == 0)
                            {
                                continue;
                            }

                            // Build the photo data
                            var photoData = BuildPhotoData(destinationFile);
                            foreach (var relatedFile in relatedFiles)
                            {
                                destinationFile = Path.Combine(destinationDirectory.FullName, Path.GetFileName(relatedFile));
                                ChangeFileDate(relatedFile, photoData.DateTaken);
                                File.Move(relatedFile, destinationFile);
                            }

                        }
                    }
                }
            }

            Console.WriteLine("All done, press any key to continue");
            Console.ReadLine();
        }

        private static void AvoidCollisionsByRenamingFiles(IEnumerable<string> allFiles)
        {
            var counter = 10000;
            foreach (var file in allFiles)
            {
                if (!File.Exists(file))
                {
                    // We may have already moved this file
                    continue;
                }

                var directoryToSearch = Path.GetDirectoryName(file);

                var filesWithSameName = Directory.GetFiles(directoryToSearch!,$"*{Path.GetFileNameWithoutExtension(file)}*.jp*");
                if (filesWithSameName.Length <= 1)
                {
                    continue;
                }

                foreach (var fileWithSameName in filesWithSameName)
                {
                    // This is the original file, leave it alone
                    if (fileWithSameName == file)
                    {
                        continue;
                    }

                    var newFileName = $"New{Path.GetFileNameWithoutExtension(fileWithSameName)}{counter++}{Path.GetExtension(file)}";
                    File.Move(fileWithSameName, newFileName);
                }
            }
        }

        private static IList<string> LookupFiles(string directoryToProcess)
        {
            var jpgFiles = Directory.GetFiles(directoryToProcess, "*.jpg");
            var jpegFiles = Directory.GetFiles(directoryToProcess, "*.jpeg");

            var totalFiles = jpgFiles.Length + jpegFiles.Length;

            if (totalFiles == 0)
            {
                return new List<string>();
            }

            var allFiles = new List<string>(jpgFiles.Length + jpegFiles.Length);
            allFiles.AddRange(jpgFiles);
            allFiles.AddRange(jpegFiles);

            return allFiles;
        }

        private static void MoveFiles(List<string> filesToMove, string parentDirectory, string subDirectory)
        {
            if (filesToMove.Count == 0)
            {
                return;
            }

            var destinationDirectory = Path.Combine(parentDirectory, subDirectory);

            Directory.CreateDirectory(destinationDirectory);
            foreach (var file in filesToMove)
            {
                var destinationFilename = $"{destinationDirectory}\\{Path.GetFileName(file)}";
                File.Move(file, destinationFilename);
            }
        }

        private static void ChangeFileDate(PhotoInformation photoData)
        {
            Debug.Assert(photoData.CanBeProcessed);
            Debug.Assert(photoData.DateTaken.HasValue);
            Debug.Assert(!string.IsNullOrEmpty(photoData.FileName));

            var fileCreationDateTime = File.GetCreationTime(photoData.FileName!);
            if (fileCreationDateTime.Date != photoData.DateTaken!.Value.Date)
            {
                ChangeFileDate(photoData.FileName, photoData.DateTaken);
            }
        }

        private static void ChangeFileDate(string file, DateTime? newDate)
        {
            if (!File.Exists(file) || !newDate.HasValue || newDate.Value == DateTime.MinValue)
            {
                return;
            }
            
            File.SetCreationTime(file, newDate.Value);
            File.SetLastWriteTime(file, newDate.Value);
        }

        private static PhotoInformation BuildPhotoData(string file)
        {
            var photoData = new PhotoInformation()
            {
                FileName = file,
            };

            try
            {
                using var reader = new ExifReader(file);

                // Try the Original Date
                TryThisDate(reader, ExifTags.DateTimeOriginal, photoData);

                if (!photoData.CanBeProcessed)
                {
                    // Could not read original, go for the digitized date
                    TryThisDate(reader, ExifTags.DateTimeDigitized, photoData);
                }
            }
            catch
            {
                photoData.CanBeProcessed = false;
            }


            return photoData;
        }

        private static void TryThisDate(ExifReader reader, ExifTags tag, PhotoInformation photoData)
        {
            reader.GetTagValue<DateTime>(tag, out var dateTime);
            photoData.DateTaken = dateTime;
            photoData.CanBeProcessed = dateTime != DateTime.MinValue;
        }
    }
}
