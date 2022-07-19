namespace redate_photo.Models
{
    using System;

    public class PhotoInformation
    {
        public string? FileName { get; set; }
        public DateTime? DateTaken { get; set; }
        public bool CanBeProcessed { get; set; } = false;
    }
}
