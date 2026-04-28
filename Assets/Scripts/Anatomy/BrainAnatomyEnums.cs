namespace VRSNC.Anatomy
{
    public enum BrainSide
    {
        Unspecified,
        Left,
        Right,
        Midline,
        Bilateral,
        ReviewRequired
    }

    public enum BrainMajorDivision
    {
        Unknown,
        Cerebro,
        Telencefalo,
        Diencefalo,
        Cerebelo,
        TroncoEncefalico,
        SistemaVentricular,
        OutrosRevisao
    }

    public enum BrainAnatomicalSubdivision
    {
        Unknown,
        CortexCerebral,
        FormacaoHipocampal,
        SistemaLimbico,
        GangliosDaBase,
        SistemaOlfatorio,
        SubstanciaBrancaComissurasETratos,
        AreaSeptalEProsencefaloBasal,
        Talamo,
        Hipotalamo,
        Epitalamo,
        Subtalamo,
        Cerebelo,
        Mesencefalo,
        Ponte,
        BulboMedulaOblonga,
        VentriculosLaterais,
        TerceiroVentriculo,
        AquedutoCerebral,
        QuartoVentriculo,
        PlexoCoroide,
        NaoClassificado,
        RevisaoAnatomicaNecessaria
    }

    public enum BrainAnatomicalSystem
    {
        Unspecified,
        Cortical,
        Limbic,
        Motor,
        Sensory,
        Olfactory,
        Ventricular,
        WhiteMatter,
        BasalGanglia,
        Diencephalic,
        Brainstem,
        Cerebellar,
        ReviewRequired
    }

    public enum BrainCorticalLobe
    {
        None,
        Frontal,
        Parietal,
        Temporal,
        Occipital,
        Insula,
        Limbic,
        Occipitotemporal,
        ReviewRequired
    }

    public enum BrainStructureReviewStatus
    {
        Classified,
        NotInCatalog,
        MissingInScene,
        DuplicateName,
        NeedsReview
    }
}
