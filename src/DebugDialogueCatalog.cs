using System;
using System.Collections.Generic;

namespace RTLC.Sable.RussianTranslate;

// Starting paths collected from the exported character configurations and
// scene InkStory components. This is data for the F4 debugger, not translation.
internal static class DebugDialogueCatalog
{
    internal static readonly IReadOnlyList<string> Knots = new[]
    {
        "aeyan_conversation",
        "altyn_conversation",
        "angler_yolanda",
        "angler_yoshi",
        "artist_costa_conversation",
        "bashir_conversation",
        "black_desert_camp_courier_ireni",
        "black_desert_camp_dude_forest",
        "black_desert_clan_merchant_trading",
        "cafe_owner_conversation",
        "cartographer_arzu",
        "cartographer_charifa",
        "cartographer_iliud",
        "cartographer_niaz",
        "cartographer_sifi",
        "cartographer_talon",
        "citra_conversation",
        "companion_conversation",
        "courier_joti_conversation",
        "dilhan_conversation",
        "doctor_val_conversation",
        "driss_conversation",
        "dock_sitter_eleni_conversation",
        "eastern_dunes_lia_conversation",
        "eccria_begger_lili",
        "eccria_carlo_conversation",
        "eccria_fin_conversation",
        "eccrine_crowd_guard_conversation",
        "eccrine_crowd_member_bemused_conversation",
        "eccrine_crowd_member_dramatic_conversation",
        "eccrine_crowd_member_nervous_conversation",
        "elaria_conversation",
        "eliisabet_conversation",
        "eliisabet_hercules_conversation",
        "eliisabet_parasite_conversation",
        "eliisabet_slicer_conversation",
        "eyries_boatworkshop_merchant_taima",
        "eyries_harvester_umi",
        "fabric_seller_kalia",
        "fir_conversation",
        "garay_conversation",
        "glassblower_toto_conversation",
        "hamza_conversation",
        "hercules_innkeeper_conversation",
        "hide_and_seek_jef",
        "hide_and_seek_nezz",
        "hide_and_seek_pala",
        "hilal_conversation",
        "iria_conversation",
        "jadi_conversation",
        "kaalan_conversation",
        "llhor_conversation",
        "mask_caster_alia_conversation",
        "mask_caster_asrar_conversation",
        "mask_caster_ezekias_conversation",
        "maz_conversation",
        "merchant_bobi",
        "merchant_habat",
        "merchant_thiago",
        "micah_conversation",
        "oasis_flower_character",
        "parasite_innkeeper_conversation",
        "petrified_forest_wanderer_vera",
        "plantweaver_rachel",
        "queen_chum_conversation",
        "researcher_casii",
        "rin_conversation",
        "saima_conversation",
        "salt_plains_felix_conversation",
        "salt_plains_harvester_dane",
        "sandip_conversation",
        "sarin_conversation",
        "sarin_at_balfron_conversation",
        "sarin_at_brunswick_conversation",
        "sarin_at_dunboyne_conversation",
        "sarin_at_rowleys_way_conversation",
        "sarin_at_shadow_of_neave_conversation",
        "sarin_at_trellicks_conversation",
        "sarin_ship_conversation",
        "serai_biker_ahvid",
        "serai_nomad_josh",
        "serai_nomad_yuuki_conversation",
        "shepherd_mel_conversation",
        "shade_of_eccria_shop_owner_milo",
        "shar_varana_conversation",
        "sizo_conversation",
        "slicer_innkeeper_conversation",
        "tohta_conversation",
        "mateo_conversation",
        "umar_conversation",
        "utarii_conversation",
        "viivi_conversation",
        "white_desert_camp_camel_herder_mateo_conversation",
        "white_desert_camp_child_leo_npc",
        "white_desert_camp_child_sofia_npc",
        "white_desert_camp_doctor",
        "white_desert_camp_ergert",
        "white_desert_camp_scrapper_jivan",
        "white_desert_vivarium_child_runa",
        "white_desert_vivarium_entertainer_edith",
        "white_desert_vivarium_merchant_sola",
        "white_desert_vivarium_parent_tyra",
        "yannis_conversation",
        "zahra_conversation",
        "zayna_conversation",
        "zebi_conversation",
        "zeki_conversation",
        "zuli_conversation"
    };

    internal static string Label(string knot)
    {
        switch (knot)
        {
            case "jadi_conversation": return "Джади";
            case "eliisabet_conversation": return "Элиисабет";
            case "eliisabet_hercules_conversation": return "Элиисабет — станция и мост";
            case "eliisabet_parasite_conversation": return "Элиисабет — лагерь Паразитов";
            case "eliisabet_slicer_conversation": return "Элиисабет — лагерь Слайсеров";
        }
        var result = knot.Replace("_conversation", "").Replace('_', ' ').Replace('.', ' ');
        return result.Length == 0 ? knot : char.ToUpperInvariant(result[0]) + result.Substring(1);
    }
}
