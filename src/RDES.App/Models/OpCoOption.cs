namespace RDES.App.Models
{
    public class OpCoOption
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public override string ToString() => Name;
    }
}
