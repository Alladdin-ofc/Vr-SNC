using UnityEngine;

namespace VRSNC.Anatomy
{
    [DisallowMultipleComponent]
    public sealed class BrainAnatomicalGroup : MonoBehaviour
    {
        [SerializeField] private string groupName;
        [SerializeField] private string groupPath;
        [SerializeField] private BrainMajorDivision majorDivision = BrainMajorDivision.Unknown;
        [SerializeField] private BrainAnatomicalSubdivision subdivision = BrainAnatomicalSubdivision.Unknown;
        [SerializeField] private string sourceRule;

        public string GroupName => groupName;
        public string GroupPath => groupPath;
        public BrainMajorDivision MajorDivision => majorDivision;
        public BrainAnatomicalSubdivision Subdivision => subdivision;
        public string SourceRule => sourceRule;

        public void SetGroup(
            string groupName,
            string groupPath,
            BrainMajorDivision majorDivision,
            BrainAnatomicalSubdivision subdivision,
            string sourceRule)
        {
            this.groupName = groupName;
            this.groupPath = groupPath;
            this.majorDivision = majorDivision;
            this.subdivision = subdivision;
            this.sourceRule = sourceRule;
        }
    }
}
