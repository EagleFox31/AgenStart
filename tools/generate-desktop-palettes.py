"""Generates src/AgenStart.Desktop/Styles/Palettes/*.axaml.

Every palette defines the same keys, so a palette can be swapped at runtime without a
missing-resource fallthrough. Edit the tables below, then run:
    python tools/generate-desktop-palettes.py [output-dir]
Running it twice produces identical files.
"""
import sys, pathlib
DEFAULT_OUT = pathlib.Path(__file__).resolve().parent.parent / "src" / "AgenStart.Desktop" / "Styles" / "Palettes"
out = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUT
ORDER = ["Nav","NavHover","NavActive","NavText","NavTextHover","NavTextActive","NavTextDisabled","NavDivider","NavStripe",
 "NavIcon","NavIconActive","NavHeading","SidebarBorder",
 "BrandText","BrandSubtitle","Surface","Card","CardBorder","Hero","HeroText","Tile","LogoTile","Text","Muted","Text3",
 "Teal","Accent","OnAccent","PrimaryHover","PrimaryPressed","SecondaryText","SecondaryBorder","SecondaryHover",
 "Fill","FillHover","Separator","Divider","IconTile",
 "Success","Warning","Danger","SoftTeal","SoftSuccess","SoftWarning","SoftDanger",
 "BadgeSuccess","BadgeSuccessBorder","BadgeWarning","BadgeSoftWarning","BadgeWarningBorder","BadgeDanger","BadgeSoftDanger","BadgeDangerBorder",
 "ProfileHover","ProfileHoverBorder","RecommendationSticky"]
T = {
"Apple": dict(desc="Apple palette: macOS grouped settings, translucent-grey sidebar, white grouped cards with soft shadows.",
 c=dict(Nav="#EFEFF2",NavHover="#1F787880",NavActive="#2E787880",NavText="#1D1D1F",NavTextHover="#1D1D1F",NavTextActive="#1D1D1F",NavTextDisabled="#A1A1A6",
 NavDivider="#1F3C3C43",NavStripe="#00000000",NavIcon="#0071E3",NavIconActive="#0071E3",NavHeading="#8E8E93",SidebarBorder="#1F3C3C43",
 BrandText="#1D1D1F",BrandSubtitle="#8E8E93",Surface="#F5F5F7",Card="#FFFFFF",CardBorder="#0F000000",Hero="#E6F0FC",HeroText="#0062C4",Tile="#F5F5F7",LogoTile="#F2F2F5",
 Text="#1D1D1F",Muted="#6E6E73",Text3="#A1A1A6",Teal="#0071E3",Accent="#0071E3",OnAccent="#FFFFFF",PrimaryHover="#0064C8",PrimaryPressed="#0057AE",
 SecondaryText="#1D1D1F",SecondaryBorder="#00000000",SecondaryHover="#2E787880",Fill="#1F787880",FillHover="#2E787880",Separator="#1F3C3C43",Divider="#E5E5EA",IconTile="#E6F0FC",
 Success="#248A3D",Warning="#C93400",Danger="#D70015",SoftTeal="#E6F0FC",SoftSuccess="#E7F5EB",SoftWarning="#FFF1E6",SoftDanger="#FDEBED",
 BadgeSuccess="#1F7A35",BadgeSuccessBorder="#00000000",BadgeWarning="#B02E00",BadgeSoftWarning="#FFF1E6",BadgeWarningBorder="#00000000",BadgeDanger="#C20013",BadgeSoftDanger="#FDEBED",BadgeDangerBorder="#00000000",
 ProfileHover="#F5F5F7",ProfileHoverBorder="#C7C7CC",RecommendationSticky="#F2F5F5F7"),
 font="Segoe UI Variable Text, Segoe UI", shadow="0 1 2 0 #0A000000, 0 8 24 0 #0F000000", cardBorder="0",
 r=dict(Small=8,Card=14,Button=8,Badge=10,NavItem=8,Group=12,Hero=16,Tile=14), navMargin="0,1", navPadding="10,0", navHeight=32),
"Google": dict(desc="Google palette: Material 3 light, tonal pill navigation, outlined rounded cards.",
 c=dict(Nav="#F0F4F9",NavHover="#E3E8EF",NavActive="#D3E3FD",NavText="#1F1F1F",NavTextHover="#1F1F1F",NavTextActive="#041E49",NavTextDisabled="#9AA0A6",
 NavDivider="#DDE3EA",NavStripe="#00000000",NavIcon="#444746",NavIconActive="#041E49",NavHeading="#5F6368",SidebarBorder="#00000000",
 BrandText="#1F1F1F",BrandSubtitle="#5F6368",Surface="#F8FAFD",Card="#FFFFFF",CardBorder="#E1E3E6",Hero="#E8F0FE",HeroText="#0B57D0",Tile="#F0F4F9",LogoTile="#F0F4F9",
 Text="#1F1F1F",Muted="#444746",Text3="#747775",Teal="#0B57D0",Accent="#0B57D0",OnAccent="#FFFFFF",PrimaryHover="#0A4FBE",PrimaryPressed="#0842A0",
 SecondaryText="#0B57D0",SecondaryBorder="#00000000",SecondaryHover="#DDE7F8",Fill="#E8EEF7",FillHover="#DDE7F8",Separator="#E9ECEF",Divider="#E1E3E6",IconTile="#E8F0FE",
 Success="#146C2E",Warning="#B05800",Danger="#B3261E",SoftTeal="#E8F0FE",SoftSuccess="#E6F4EA",SoftWarning="#FEF3E0",SoftDanger="#FCE8E6",
 BadgeSuccess="#146C2E",BadgeSuccessBorder="#00000000",BadgeWarning="#8A4A00",BadgeSoftWarning="#FEF3E0",BadgeWarningBorder="#00000000",BadgeDanger="#B3261E",BadgeSoftDanger="#FCE8E6",BadgeDangerBorder="#00000000",
 ProfileHover="#F0F4F9",ProfileHoverBorder="#C4C7C5",RecommendationSticky="#F2F8FAFD"),
 font="Google Sans, Roboto, Segoe UI Variable Text, Segoe UI", shadow="0 1 2 0 #0D000000", cardBorder="1",
 r=dict(Small=12,Card=16,Button=20,Badge=8,NavItem=18,Group=16,Hero=24,Tile=16), navMargin="0,1", navPadding="16,0", navHeight=36),
"AgenStart": dict(desc="AgenStart palette: the brand teal on a light, calm workspace.",
 c=dict(Nav="#EEF3F2",NavHover="#E2EBE9",NavActive="#D7E7E3",NavText="#1E2F33",NavTextHover="#14262B",NavTextActive="#0F4F48",NavTextDisabled="#9AA8AB",
 NavDivider="#DCE5E3",NavStripe="#00000000",NavIcon="#176D64",NavIconActive="#0F4F48",NavHeading="#7C8C90",SidebarBorder="#DCE5E3",
 BrandText="#14262B",BrandSubtitle="#7C8C90",Surface="#F6F8F8",Card="#FFFFFF",CardBorder="#E3EAE9",Hero="#E4F1EE",HeroText="#0F5A52",Tile="#F1F5F4",LogoTile="#EDF3F2",
 Text="#14262B",Muted="#5B6B70",Text3="#94A3A7",Teal="#176D64",Accent="#176D64",OnAccent="#FFFFFF",PrimaryHover="#135E56",PrimaryPressed="#0F4F48",
 SecondaryText="#14262B",SecondaryBorder="#00000000",SecondaryHover="#DDE7E5",Fill="#EAF0EF",FillHover="#DDE7E5",Separator="#E6ECEB",Divider="#E0E7E6",IconTile="#E4F1EE",
 Success="#1F7A4D",Warning="#A65A00",Danger="#C0262D",SoftTeal="#E4F1EE",SoftSuccess="#E5F4EC",SoftWarning="#FDF2E2",SoftDanger="#FBE9EA",
 BadgeSuccess="#1F7A4D",BadgeSuccessBorder="#00000000",BadgeWarning="#8F4D00",BadgeSoftWarning="#FDF2E2",BadgeWarningBorder="#00000000",BadgeDanger="#B0222A",BadgeSoftDanger="#FBE9EA",BadgeDangerBorder="#00000000",
 ProfileHover="#F1F5F4",ProfileHoverBorder="#B9C9C6",RecommendationSticky="#F2F6F8F8"),
 font="Segoe UI Variable Text, Segoe UI", shadow="0 1 2 0 #0A0B2A2E, 0 6 18 0 #0D0B2A2E", cardBorder="1",
 r=dict(Small=8,Card=12,Button=8,Badge=10,NavItem=8,Group=12,Hero=14,Tile=12), navMargin="0,1", navPadding="10,0", navHeight=32),
"Dark": dict(variant="Dark", desc="Dark palette: the AgenStart teal on deep neutral surfaces.",
 c=dict(Nav="#1A1D1E",NavHover="#24292A",NavActive="#1F3A36",NavText="#D9E2E0",NavTextHover="#F0F5F4",NavTextActive="#7FD8CB",NavTextDisabled="#5E6B6D",
 NavDivider="#2C3233",NavStripe="#00000000",NavIcon="#4FC3B4",NavIconActive="#7FD8CB",NavHeading="#7D8B8E",SidebarBorder="#2A3031",
 BrandText="#EEF3F2",BrandSubtitle="#8A9799",Surface="#141718",Card="#1E2223",CardBorder="#2C3334",Hero="#173430",HeroText="#8EE0D3",Tile="#1E2223",LogoTile="#2A3031",
 Text="#E8EEED",Muted="#A3B0B2",Text3="#6F7C7F",Teal="#3FB7A8",Accent="#3FB7A8",OnAccent="#06201D",PrimaryHover="#52C4B6",PrimaryPressed="#2E9E90",
 SecondaryText="#E8EEED",SecondaryBorder="#00000000",SecondaryHover="#394143",Fill="#2C3335",FillHover="#394143",Separator="#2A3032",Divider="#2F3638",IconTile="#173430",
 Success="#4CC38A",Warning="#E3A44B",Danger="#F2777B",SoftTeal="#173430",SoftSuccess="#16302A",SoftWarning="#3A2D17",SoftDanger="#3B1F21",
 BadgeSuccess="#6FD3A2",BadgeSuccessBorder="#00000000",BadgeWarning="#F0B866",BadgeSoftWarning="#3A2D17",BadgeWarningBorder="#00000000",BadgeDanger="#F58C90",BadgeSoftDanger="#3B1F21",BadgeDangerBorder="#00000000",
 ProfileHover="#242A2B",ProfileHoverBorder="#4A5658",RecommendationSticky="#F2141718"),
 font="Segoe UI Variable Text, Segoe UI", shadow="0 1 2 0 #33000000, 0 8 24 0 #40000000", cardBorder="1",
 r=dict(Small=8,Card=12,Button=8,Badge=10,NavItem=8,Group=12,Hero=14,Tile=12), navMargin="0,1", navPadding="10,0", navHeight=32),
}
for name, t in T.items():
    c = t["c"]; assert set(c) == set(ORDER), (name, set(ORDER) ^ set(c))
    L = ['<ResourceDictionary xmlns="https://github.com/avaloniaui"', '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
         f'  <!-- {t["desc"]} Every theme defines the same keys. -->']
    for k in ORDER:
        L.append(f'  <Color x:Key="Agen{k}Color">{c[k]}</Color>')
        L.append(f'  <SolidColorBrush x:Key="Agen{k}Brush" Color="{c[k]}" />')
    L.append(f'  <Color x:Key="AgenFluentAccentColor">{c["Accent"]}</Color>')
    L.append(f'  <x:String x:Key="AgenThemeVariant">{t.get("variant", "Light")}</x:String>')
    L.append(f'  <FontFamily x:Key="AgenFontFamily">{t["font"]}</FontFamily>')
    L.append(f'  <BoxShadows x:Key="AgenCardShadow">{t["shadow"]}</BoxShadows>')
    L.append(f'  <Thickness x:Key="AgenCardBorderThickness">{t["cardBorder"]}</Thickness>')
    for k, v in t["r"].items():
        L.append(f'  <CornerRadius x:Key="Agen{k}Radius">{v}</CornerRadius>')
    L.append(f'  <Thickness x:Key="AgenNavItemMargin">{t["navMargin"]}</Thickness>')
    L.append(f'  <Thickness x:Key="AgenNavItemPadding">{t["navPadding"]}</Thickness>')
    L.append(f'  <x:Double x:Key="AgenNavItemHeight">{t["navHeight"]}</x:Double>')
    L.append('</ResourceDictionary>')
    (out / f"{name}.axaml").write_text("\n".join(L) + "\n", encoding="utf-8")
print(f"wrote {len(T)} palettes to {out}")
