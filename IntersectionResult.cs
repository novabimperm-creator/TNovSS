using Autodesk.Revit.DB;

namespace TNovSS
{
    public class IntersectionResult
    {
        public ElementId CurrentElementId { get; set; }
        public ElementId LinkedElementId { get; set; }
        public string LinkDocumentName { get; set; }
        public ElementId LinkInstanceId { get; set; }
        public string CurrentElementName { get; set; }
        public string LinkedElementName { get; set; }
    }
}