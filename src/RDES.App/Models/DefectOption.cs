namespace RDES.App.Models
{
    public class DefectOption
    {
        public long Id { get; set; }
        public string Category { get; set; } = "General";
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public override string ToString() => Name;
    }
}
