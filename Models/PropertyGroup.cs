using System.Collections.Generic;

namespace UmbracoCsvImport.Models
{
    public class PropertyGroup
    {
        public string Name { get; set; }
        public List<PropertyType> PropertyTypes { get; set; }
    }
}