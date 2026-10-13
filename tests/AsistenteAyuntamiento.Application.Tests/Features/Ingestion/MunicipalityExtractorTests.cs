using AsistenteAyuntamiento.Application.Features.Ingestion;
using FluentAssertions;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>Municipality of BOE/BOJA documents from their issuing body or title (spec: hierarchical-retrieval).</summary>
public class MunicipalityExtractorTests
{
    [Theory]
    // BOE section V-B: the department is the Ayuntamiento, in capitals.
    [InlineData("Anuncio del Ayuntamiento de Sedella sobre aprobación definitiva del presupuesto general.", "AYUNTAMIENTO DE SEDELLA", "Sedella")]
    [InlineData("Anuncio de licitación del Ayuntamiento de Vélez-Málaga. Objeto: Servicio de limpieza viaria.", "AYUNTAMIENTO DE VÉLEZ-MÁLAGA", "Vélez-Málaga")]
    [InlineData("Anuncio de formalización de contratos", "AYUNTAMIENTO DE LA LÍNEA DE LA CONCEPCIÓN", "La Línea de la Concepción")]
    [InlineData("Anuncio de licitación", "AYUNTAMIENTO DE EL EJIDO", "El Ejido")]
    [InlineData("Anuncio de licitación", "Ayuntamiento de Las Palmas de Gran Canaria", "Las Palmas de Gran Canaria")]
    [InlineData("Anuncio de licitación", "AYUNTAMIENTO DEL PUERTO DE SANTA MARÍA", "El Puerto de Santa María")]
    [InlineData("Anuncio de licitación", "Excmo. Ayuntamiento de Ronda", "Ronda")]
    // BOE section II.B / III (Administración Local): the issuer is in the title, with the province.
    [InlineData("Resolución de 3 de marzo de 2026, del Ayuntamiento de Alhaurín de la Torre (Málaga), referente a la convocatoria para proveer varias plazas.", "Administración Local", "Alhaurín de la Torre")]
    [InlineData("Resolución de 12 de enero de 2026, del Ayuntamiento de Los Palacios y Villafranca (Sevilla), referente a la convocatoria para proveer una plaza.", "Administración Local", "Los Palacios y Villafranca")]
    // BOJA: the department is always the Junta; the title names the Ayuntamiento after the date.
    [InlineData("Anuncio de 3 de febrero de 2026, del Ayuntamiento de Sedella, de aprobación inicial del Plan General.", "Junta de Andalucía", "Sedella")]
    [InlineData("BOJA nº 17 - Anuncio de 20 de enero de 2026, del Ayuntamiento de El Ejido, por el que se publican las bases.", "Junta de Andalucía", "El Ejido")]
    [InlineData("Edicto de 5 de mayo de 2026, del Ayuntamiento de Benalmádena (Málaga) por el que se somete a información pública el estudio de detalle.", "Junta de Andalucía", "Benalmádena")]
    [InlineData("Anuncio de 2 de abril de 2026, del Ayuntamiento de VÉLEZ-MÁLAGA, sobre la oferta de empleo público.", "Junta de Andalucía", "Vélez-Málaga")]
    public void Finds_the_municipality_of_documents_issued_by_an_Ayuntamiento(string title, string department, string expected) =>
        MunicipalityExtractor.Extract(title, department).Should().Be(expected);

    [Theory]
    // State and regional bodies: the norm applies everywhere.
    [InlineData("Real Decreto 100/2026, de 3 de febrero, de ayudas al alquiler", "Ministerio de Vivienda y Agenda Urbana")]
    [InlineData("Orden de 3 de febrero de 2026, por la que se aprueban las bases reguladoras de subvenciones.", "Junta de Andalucía")]
    // Supramunicipal bodies.
    [InlineData("Anuncio de la Diputación Provincial de Málaga sobre convocatoria de subvenciones.", "DIPUTACIÓN PROVINCIAL DE MÁLAGA")]
    [InlineData("Anuncio de 10 de marzo de 2026, de la Mancomunidad de Municipios de la Costa del Sol Occidental, de aprobación del presupuesto.", "Junta de Andalucía")]
    [InlineData("Anuncio de licitación", "CONSORCIO PROVINCIAL DE BOMBEROS DE MÁLAGA")]
    // Ayuntamientos mentioned outside the issuer: the document is not theirs.
    [InlineData("Resolución de 5 de mayo de 2026, de la Dirección General de Administración Local, por la que se admite la inscripción del escudo del Ayuntamiento de Sedella.", "Junta de Andalucía")]
    [InlineData("Resolución de 2 de febrero de 2026, de la Secretaría General Técnica, por la que se publica el Convenio con el Ayuntamiento de Ronda.", "Ministerio del Interior")]
    [InlineData("Acuerdo de 3 de febrero de 2026, del Consejo de Gobierno, por el que se autoriza al Ayuntamiento de Marbella a enajenar una parcela.", "Junta de Andalucía")]
    // Several Ayuntamientos, organs of an Ayuntamiento or nothing to go on.
    [InlineData("Anuncio de 3 de febrero de 2026, de los Ayuntamientos de Sedella y Canillas de Aceituno, de deslinde.", "Junta de Andalucía")]
    [InlineData("Anuncio de 3 de febrero de 2026, de la Gerencia Municipal de Urbanismo del Ayuntamiento de Marbella, de información pública.", "Junta de Andalucía")]
    [InlineData("Documento BOJA", "Junta de Andalucía")]
    [InlineData("", "")]
    public void Returns_null_when_the_document_is_not_clearly_from_one_municipality(string title, string department) =>
        MunicipalityExtractor.Extract(title, department).Should().BeNull();

    [Fact]
    public void The_department_wins_over_the_title() =>
        MunicipalityExtractor.Extract("Anuncio de 3 de febrero de 2026, del Ayuntamiento de Ronda, de licitación.", "AYUNTAMIENTO DE SEDELLA")
            .Should().Be("Sedella");

    [Fact]
    public void Null_metadata_has_no_municipality() =>
        MunicipalityExtractor.Extract(null, null).Should().BeNull();

    [Theory]
    [InlineData("Vélez-Málaga", "velez malaga")]
    [InlineData("VELEZ MALAGA", "velez malaga")]
    [InlineData("Ayuntamiento de Vélez-Málaga (Málaga)", "velez malaga")]
    [InlineData("  alhaurín DE la torre ", "alhaurin de la torre")]
    [InlineData("Ayuntamiento del Puerto de Santa María", "puerto de santa maria")]
    [InlineData("Sant Joan d'Alacant", "sant joan d alacant")]
    [InlineData("Peñarroya-Pueblonuevo", "penarroya pueblonuevo")]
    public void The_match_key_ignores_case_accents_punctuation_and_the_Ayuntamiento_prefix(string value, string expected) =>
        MunicipalityExtractor.ToMatchKey(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(Málaga)")]
    public void An_empty_value_has_no_match_key(string? value) =>
        MunicipalityExtractor.ToMatchKey(value).Should().BeNull();

    [Fact]
    public void The_key_of_an_extracted_name_is_found_in_the_key_of_a_citizen_s_spelling()
    {
        var stored = MunicipalityExtractor.ToMatchKey(MunicipalityExtractor.Extract("Anuncio", "AYUNTAMIENTO DE VÉLEZ-MÁLAGA"))!;

        stored.Should().Contain(MunicipalityExtractor.ToMatchKey("velez malaga")!)
            .And.Contain(MunicipalityExtractor.ToMatchKey("Vélez")!);
    }
}
