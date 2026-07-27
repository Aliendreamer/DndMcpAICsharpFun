using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public class BookCatalogTests
{
    [Fact]
    public void Keys_and_display_names_round_trip()
    {
        BookCatalog.KeyToDisplayName["DMG"].Should().Be("Dungeon Master's Guide 2014");
        BookCatalog.DisplayNameToKey["Dungeon Master's Guide 2014"].Should().Be("DMG");
        BookCatalog.DisplayNameToKey["PlayerHandbook 2014"].Should().Be("PHB");
    }

    [Fact]
    public void ToDisplayNames_maps_scope_keys_back_to_citation_names()
    {
        // ScopedBooks in tool results must be human/LLM-facing display names, not the terse
        // filtering keys — the passages cite "Dungeon Master's Guide 2014", so the scope summary must too.
        BookCatalog.ToDisplayNames(new[] { "DMG", "PHB" })
            .Should().Equal("Dungeon Master's Guide 2014", "PlayerHandbook 2014");
        BookCatalog.ToDisplayNames(new[] { "XGE" }).Should().ContainSingle()
            .Which.Should().Be("Xanathar's Guide to Everything");
    }

    [Fact]
    public void Keys_are_unique_and_nonempty()
    {
        BookCatalog.All.Should().OnlyContain(b =>
            !string.IsNullOrWhiteSpace(b.Key) && !string.IsNullOrWhiteSpace(b.DisplayName));
        BookCatalog.Keys.Count.Should().Be(BookCatalog.All.Count);
    }

    [Fact]
    public void MPMM_MTF_SCAG_are_registered_with_verbatim_display_names()
    {
        // These three were extracted (canonical JSON produced) before ever being wired into
        // BookCatalog — the audit's catalog-drift finding. Registering them here closes that gap.
        BookCatalog.KeyToDisplayName["SCAG"].Should().Be("Sword Coast Adventurer's Guide");
        BookCatalog.KeyToDisplayName["MTF"].Should().Be("Mordenkainen's Tome of Foes");
        BookCatalog.KeyToDisplayName["MPMM"].Should().Be("Mordenkainen Presents: Monsters of the Multiverse");

        BookCatalog.DisplayNameToKey["Sword Coast Adventurer's Guide"].Should().Be("SCAG");
        BookCatalog.DisplayNameToKey["Mordenkainen's Tome of Foes"].Should().Be("MTF");
        BookCatalog.DisplayNameToKey["Mordenkainen Presents: Monsters of the Multiverse"].Should().Be("MPMM");
    }

    [Fact]
    public void FTD_and_BGG_are_registered_with_verbatim_display_names()
    {
        BookCatalog.KeyToDisplayName["FTD"].Should().Be("Fizban's Treasury of Dragons");
        BookCatalog.KeyToDisplayName["BGG"].Should().Be("Bigby Presents: Glory of the Giants");
        BookCatalog.DisplayNameToKey["Fizban's Treasury of Dragons"].Should().Be("FTD");
        BookCatalog.DisplayNameToKey["Bigby Presents: Glory of the Giants"].Should().Be("BGG");
    }

    [Fact]
    public void EGW_and_SCC_are_registered_with_verbatim_display_names()
    {
        BookCatalog.KeyToDisplayName["EGW"].Should().Be("Explorer's Guide to Wildemount");
        BookCatalog.KeyToDisplayName["SCC"].Should().Be("Strixhaven: A Curriculum of Chaos");
        BookCatalog.DisplayNameToKey["Explorer's Guide to Wildemount"].Should().Be("EGW");
        BookCatalog.DisplayNameToKey["Strixhaven: A Curriculum of Chaos"].Should().Be("SCC");
    }

    [Fact]
    public void Revised_core_2024_books_are_registered_with_Edition2024()
    {
        // Registered ahead of ingest so the drift guard recognises their source_key immediately.
        BookCatalog.KeyToDisplayName["XPHB"].Should().Be("Player's Handbook 2024");
        BookCatalog.KeyToDisplayName["XDMG"].Should().Be("Dungeon Master's Guide 2024");
        BookCatalog.KeyToDisplayName["XMM"].Should().Be("Monster Manual 2025");

        BookCatalog.All.Where(b => b.Key is "XPHB" or "XDMG" or "XMM")
            .Should().OnlyContain(b => b.Version == DndVersion.Edition2024)
            .And.HaveCount(3);
    }

    [Fact]
    public void Revised_core_display_names_agree_with_the_EntityIdSlug_override_table()
    {
        // The canonical file slug and the dnd_blocks payload must agree, so the catalog display name
        // has to be a key the override table recognises — otherwise registering with that displayName
        // slugifies it instead ("playerhandbook-2024.json"), the mis-named-file gotcha.
        EntityIdSlug.BookSlug(BookCatalog.KeyToDisplayName["XPHB"]).Should().Be("phb24");
        EntityIdSlug.BookSlug(BookCatalog.KeyToDisplayName["XDMG"]).Should().Be("dmg24");
        EntityIdSlug.BookSlug(BookCatalog.KeyToDisplayName["XMM"]).Should().Be("mm24");

        // ...and the 5etools source keys must slug identically, since ids derive from the KEY.
        EntityIdSlug.BookSlug("XPHB").Should().Be("phb24");
        EntityIdSlug.BookSlug("XDMG").Should().Be("dmg24");
        EntityIdSlug.BookSlug("XMM").Should().Be("mm24",
            "XMM is the real 5etools key; the old MM25 entry matched nothing, so a registration would "
            + "have produced xmm.monster.* instead of mm24.monster.*");
    }

    [Fact]
    public void EEPC_is_registered_so_its_blocks_can_be_scoped()
    {
        // Elemental Evil was registered without a fivetoolsSourceKey, so its 365 blocks carry no
        // source_key and the drift guard counted them. Registering it here lets the source-key
        // backfill stamp them (it maps by display name) without touching its entity ids.
        BookCatalog.KeyToDisplayName["EEPC"].Should().Be("Elemental Evil Player's Companion");
        BookCatalog.DisplayNameToKey["Elemental Evil Player's Companion"].Should().Be("EEPC");
    }

    [Fact]
    public void TCE_is_registered_with_verbatim_display_name()
    {
        // TCE was ingested (2445 blocks, source_key="TCE") but never registered here, so every
        // key-scoped retrieval feature was blind to it and the startup catalog-drift guard warned
        // about 2445 unknown-source_key points. Registering it closes that gap.
        BookCatalog.KeyToDisplayName["TCE"].Should().Be("Tasha's Cauldron of Everything");
        BookCatalog.DisplayNameToKey["Tasha's Cauldron of Everything"].Should().Be("TCE");
    }

    [Fact]
    public void GGR_is_registered_with_verbatim_display_name()
    {
        BookCatalog.KeyToDisplayName["GGR"].Should().Be("Guildmasters' Guide to Ravnica");
        BookCatalog.DisplayNameToKey["Guildmasters' Guide to Ravnica"].Should().Be("GGR");
    }

    [Fact]
    public void VRGR_is_registered_with_verbatim_display_name()
    {
        // Ravenloft setting book — wired into BookCatalog so setting-lore citations resolve to a
        // display name and the scope-health guard knows the key (SettingCatalog maps Ravenloft -> VRGR).
        BookCatalog.KeyToDisplayName["VRGR"].Should().Be("Van Richten's Guide to Ravenloft");
        BookCatalog.DisplayNameToKey["Van Richten's Guide to Ravenloft"].Should().Be("VRGR");
    }
}