using System.Collections.Generic;

namespace UmbracoCsvImport.Models
{
    public class Variant
    {
        public Language Language { get; set; }
        public List<PropertyGroup> PropertyGroups { get; set; }
    }
}