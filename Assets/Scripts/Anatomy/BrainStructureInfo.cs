using UnityEngine;

namespace VRSNC.Anatomy
{
    [DisallowMultipleComponent]
    public sealed class BrainStructureInfo : MonoBehaviour
    {
        [SerializeField] private string anatomicalName;
        [SerializeField] private string portugueseName;
        [SerializeField] private string originalScenePath;
        [SerializeField] private string originalParentGroup;
        [SerializeField] private BrainMajorDivision majorDivision = BrainMajorDivision.Unknown;
        [SerializeField] private BrainAnatomicalSubdivision subdivision = BrainAnatomicalSubdivision.Unknown;
        [SerializeField] private BrainAnatomicalSystem anatomicalGroup = BrainAnatomicalSystem.Unspecified;
        [SerializeField] private BrainCorticalLobe corticalLobe = BrainCorticalLobe.None;
        [SerializeField] private BrainSide hemisphereOrSpatialDivision = BrainSide.Unspecified;
        [SerializeField] private int layerIndex = -1;
        [SerializeField] private string shortDescription;
        [TextArea(2, 5)]
        [SerializeField] private string functionalDescription;
        [SerializeField] private BrainStructureReviewStatus reviewStatus = BrainStructureReviewStatus.NeedsReview;
        [SerializeField] private string sourceObjectName;
        [SerializeField] private string sourceCatalogDivision;
        [SerializeField] private string sourceCatalogSubdivision;

        public string AnatomicalName => anatomicalName;
        public string PortugueseName => portugueseName;
        public string OriginalScenePath => originalScenePath;
        public string OriginalParentGroup => originalParentGroup;
        public BrainMajorDivision MajorDivision => majorDivision;
        public BrainAnatomicalSubdivision Subdivision => subdivision;
        public BrainAnatomicalSystem AnatomicalGroup => anatomicalGroup;
        public BrainCorticalLobe CorticalLobe => corticalLobe;
        public BrainSide HemisphereOrSpatialDivision => hemisphereOrSpatialDivision;
        public int LayerIndex => layerIndex;
        public string ShortDescription => shortDescription;
        public string FunctionalDescription => functionalDescription;
        public BrainStructureReviewStatus ReviewStatus => reviewStatus;
        public string SourceObjectName => sourceObjectName;
        public string SourceCatalogDivision => sourceCatalogDivision;
        public string SourceCatalogSubdivision => sourceCatalogSubdivision;

        public void SetMetadata(
            string anatomicalName,
            string portugueseName,
            string originalScenePath,
            string originalParentGroup,
            BrainMajorDivision majorDivision,
            BrainAnatomicalSubdivision subdivision,
            BrainAnatomicalSystem anatomicalGroup,
            BrainCorticalLobe corticalLobe,
            BrainSide hemisphereOrSpatialDivision,
            int layerIndex,
            string shortDescription,
            string functionalDescription,
            BrainStructureReviewStatus reviewStatus,
            string sourceObjectName,
            string sourceCatalogDivision,
            string sourceCatalogSubdivision)
        {
            this.anatomicalName = anatomicalName;
            this.portugueseName = portugueseName;
            this.originalScenePath = originalScenePath;
            this.originalParentGroup = originalParentGroup;
            this.majorDivision = majorDivision;
            this.subdivision = subdivision;
            this.anatomicalGroup = anatomicalGroup;
            this.corticalLobe = corticalLobe;
            this.hemisphereOrSpatialDivision = hemisphereOrSpatialDivision;
            this.layerIndex = layerIndex;
            this.shortDescription = shortDescription;
            this.functionalDescription = functionalDescription;
            this.reviewStatus = reviewStatus;
            this.sourceObjectName = sourceObjectName;
            this.sourceCatalogDivision = sourceCatalogDivision;
            this.sourceCatalogSubdivision = sourceCatalogSubdivision;
        }
    }
}
