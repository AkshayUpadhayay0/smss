using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>One selectable entry of the import catalog.</summary>
    public class MasterImportItemDto
    {
        /// <summary>What the client sends back to select it (the name; for document types the code).</summary>
        public string Key { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Code { get; set; }
        /// <summary>Document types only: STUDENT / EMPLOYEE / BOTH.</summary>
        public string? AppliesTo { get; set; }
        /// <summary>Document types only.</summary>
        public bool? IsRequired { get; set; }
        /// <summary>The school already has it, so importing would skip it.</summary>
        public bool AlreadyAdded { get; set; }
    }

    public class MasterImportCatalogDto
    {
        public string DefaultSectionName { get; set; } = null!;
        public List<MasterImportItemDto> Classes { get; set; } = new();
        public List<MasterImportItemDto> Subjects { get; set; } = new();
        public List<MasterImportItemDto> Designations { get; set; } = new();
        public List<MasterImportItemDto> Departments { get; set; } = new();
        public List<MasterImportItemDto> AdmissionTypes { get; set; } = new();
        public List<MasterImportItemDto> DocumentTypes { get; set; } = new();
    }

    /// <summary>The keys the school selected. No school id: it comes from the token.</summary>
    public class ImportMastersRequest
    {
        [MaxLength(100)] public List<string> Classes { get; set; } = new();
        [MaxLength(100)] public List<string> Subjects { get; set; } = new();
        [MaxLength(100)] public List<string> Designations { get; set; } = new();
        [MaxLength(100)] public List<string> Departments { get; set; } = new();
        [MaxLength(100)] public List<string> AdmissionTypes { get; set; } = new();
        [MaxLength(100)] public List<string> DocumentTypes { get; set; } = new();
    }

    public class ImportCountDto
    {
        public int Created { get; set; }
        /// <summary>Already there (or, for sections, the class already has sections), so left untouched.</summary>
        public int Skipped { get; set; }
    }

    public class ImportMastersResultDto
    {
        public ImportCountDto Classes { get; set; } = new();
        public ImportCountDto Sections { get; set; } = new();
        public ImportCountDto Subjects { get; set; } = new();
        public ImportCountDto Designations { get; set; } = new();
        public ImportCountDto Departments { get; set; } = new();
        public ImportCountDto AdmissionTypes { get; set; } = new();
        public ImportCountDto DocumentTypes { get; set; } = new();
        public int TotalCreated =>
            Classes.Created + Sections.Created + Subjects.Created + Designations.Created + Departments.Created + AdmissionTypes.Created + DocumentTypes.Created;
    }
}
