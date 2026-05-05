namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// Türkiye'nin 81 ili — plate code + name. Resmi NVI verisi.
/// Source: https://www.nvi.gov.tr (resmi liste).
/// </summary>
internal static class Tr81CitySeed
{
    public static readonly IReadOnlyList<(int PlateCode, string Name)> Cities = new[]
    {
        (1, "Adana"), (2, "Adıyaman"), (3, "Afyonkarahisar"), (4, "Ağrı"), (5, "Amasya"),
        (6, "Ankara"), (7, "Antalya"), (8, "Artvin"), (9, "Aydın"), (10, "Balıkesir"),
        (11, "Bilecik"), (12, "Bingöl"), (13, "Bitlis"), (14, "Bolu"), (15, "Burdur"),
        (16, "Bursa"), (17, "Çanakkale"), (18, "Çankırı"), (19, "Çorum"), (20, "Denizli"),
        (21, "Diyarbakır"), (22, "Edirne"), (23, "Elazığ"), (24, "Erzincan"), (25, "Erzurum"),
        (26, "Eskişehir"), (27, "Gaziantep"), (28, "Giresun"), (29, "Gümüşhane"), (30, "Hakkari"),
        (31, "Hatay"), (32, "Isparta"), (33, "Mersin"), (34, "İstanbul"), (35, "İzmir"),
        (36, "Kars"), (37, "Kastamonu"), (38, "Kayseri"), (39, "Kırklareli"), (40, "Kırşehir"),
        (41, "Kocaeli"), (42, "Konya"), (43, "Kütahya"), (44, "Malatya"), (45, "Manisa"),
        (46, "Kahramanmaraş"), (47, "Mardin"), (48, "Muğla"), (49, "Muş"), (50, "Nevşehir"),
        (51, "Niğde"), (52, "Ordu"), (53, "Rize"), (54, "Sakarya"), (55, "Samsun"),
        (56, "Siirt"), (57, "Sinop"), (58, "Sivas"), (59, "Tekirdağ"), (60, "Tokat"),
        (61, "Trabzon"), (62, "Tunceli"), (63, "Şanlıurfa"), (64, "Uşak"), (65, "Van"),
        (66, "Yozgat"), (67, "Zonguldak"), (68, "Aksaray"), (69, "Bayburt"), (70, "Karaman"),
        (71, "Kırıkkale"), (72, "Batman"), (73, "Şırnak"), (74, "Bartın"), (75, "Ardahan"),
        (76, "Iğdır"), (77, "Yalova"), (78, "Karabük"), (79, "Kilis"), (80, "Osmaniye"),
        (81, "Düzce"),
    };

    /// <summary>
    /// 15 büyük şehir için sample district seti — comprehensive demo seeder bunlara salon yerleştirir.
    /// Tüm 970+ ilçe import'u (NVI tam veri seti) Phase 1+ runbook'ta.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string[]> SampleDistricts = new Dictionary<int, string[]>
    {
        [34] = new[] // İstanbul (39 ilçe)
        {
            "Adalar", "Arnavutköy", "Ataşehir", "Avcılar", "Bağcılar", "Bahçelievler",
            "Bakırköy", "Başakşehir", "Bayrampaşa", "Beşiktaş", "Beykoz", "Beylikdüzü",
            "Beyoğlu", "Büyükçekmece", "Çatalca", "Çekmeköy", "Esenler", "Esenyurt",
            "Eyüpsultan", "Fatih", "Gaziosmanpaşa", "Güngören", "Kadıköy", "Kağıthane",
            "Kartal", "Küçükçekmece", "Maltepe", "Pendik", "Sancaktepe", "Sarıyer",
            "Silivri", "Sultanbeyli", "Sultangazi", "Şile", "Şişli", "Tuzla",
            "Ümraniye", "Üsküdar", "Zeytinburnu",
        },
        [6] = new[] // Ankara (25 ilçe)
        {
            "Akyurt", "Altındağ", "Ayaş", "Bala", "Beypazarı", "Çamlıdere", "Çankaya",
            "Çubuk", "Elmadağ", "Etimesgut", "Evren", "Gölbaşı", "Güdül", "Haymana",
            "Kalecik", "Kazan", "Keçiören", "Kızılcahamam", "Mamak", "Nallıhan",
            "Polatlı", "Pursaklar", "Sincan", "Şereflikoçhisar", "Yenimahalle",
        },
        [35] = new[] // İzmir (30 ilçe)
        {
            "Aliağa", "Balçova", "Bayındır", "Bayraklı", "Bergama", "Beydağ",
            "Bornova", "Buca", "Çeşme", "Çiğli", "Dikili", "Foça", "Gaziemir",
            "Güzelbahçe", "Karabağlar", "Karaburun", "Karşıyaka", "Kemalpaşa",
            "Kınık", "Kiraz", "Konak", "Menderes", "Menemen", "Narlıdere",
            "Ödemiş", "Seferihisar", "Selçuk", "Tire", "Torbalı", "Urla",
        },
        [16] = new[] // Bursa (17 ilçe)
        {
            "Büyükorhan", "Gemlik", "Gürsu", "Harmancık", "İnegöl", "İznik",
            "Karacabey", "Keles", "Kestel", "Mudanya", "Mustafakemalpaşa",
            "Nilüfer", "Orhaneli", "Orhangazi", "Osmangazi", "Yenişehir", "Yıldırım",
        },
        [7] = new[] // Antalya (19 ilçe)
        {
            "Akseki", "Aksu", "Alanya", "Demre", "Döşemealtı", "Elmalı", "Finike",
            "Gazipaşa", "Gündoğmuş", "İbradı", "Kaş", "Kemer", "Kepez", "Konyaaltı",
            "Korkuteli", "Kumluca", "Manavgat", "Muratpaşa", "Serik",
        },
        [1] = new[] // Adana (15 ilçe)
        {
            "Aladağ", "Ceyhan", "Çukurova", "Feke", "İmamoğlu", "Karaisalı", "Karataş",
            "Kozan", "Pozantı", "Saimbeyli", "Sarıçam", "Seyhan", "Tufanbeyli",
            "Yumurtalık", "Yüreğir",
        },
        [42] = new[] // Konya (31 ilçe)
        {
            "Akören", "Akşehir", "Altınekin", "Beyşehir", "Bozkır", "Cihanbeyli",
            "Çeltik", "Çumra", "Derbent", "Derebucak", "Doğanhisar", "Emirgazi",
            "Ereğli", "Güneysınır", "Hadim", "Halkapınar", "Hüyük", "Ilgın",
            "Kadınhanı", "Karapınar", "Karatay", "Kulu", "Meram", "Sarayönü",
            "Selçuklu", "Seydişehir", "Taşkent", "Tuzlukçu", "Yalıhüyük", "Yunak",
            "Akyokuş",
        },
        [27] = new[] // Gaziantep (9 ilçe)
        {
            "Araban", "İslahiye", "Karkamış", "Nizip", "Nurdağı", "Oğuzeli",
            "Şahinbey", "Şehitkamil", "Yavuzeli",
        },
        [38] = new[] // Kayseri (16 ilçe)
        {
            "Akkışla", "Bünyan", "Develi", "Felahiye", "Hacılar", "İncesu",
            "Kocasinan", "Melikgazi", "Özvatan", "Pınarbaşı", "Sarıoğlan",
            "Sarız", "Talas", "Tomarza", "Yahyalı", "Yeşilhisar",
        },
        [33] = new[] // Mersin (13 ilçe)
        {
            "Akdeniz", "Anamur", "Aydıncık", "Bozyazı", "Çamlıyayla", "Erdemli",
            "Gülnar", "Mezitli", "Mut", "Silifke", "Tarsus", "Toroslar", "Yenişehir",
        },
        [26] = new[] // Eskişehir (14 ilçe)
        {
            "Alpu", "Beylikova", "Çifteler", "Günyüzü", "Han", "İnönü", "Mahmudiye",
            "Mihalgazi", "Mihalıççık", "Odunpazarı", "Sarıcakaya", "Seyitgazi",
            "Sivrihisar", "Tepebaşı",
        },
        [61] = new[] // Trabzon (18 ilçe)
        {
            "Akçaabat", "Araklı", "Arsin", "Beşikdüzü", "Çarşıbaşı", "Çaykara",
            "Dernekpazarı", "Düzköy", "Hayrat", "Köprübaşı", "Maçka", "Of",
            "Ortahisar", "Şalpazarı", "Sürmene", "Tonya", "Vakfıkebir", "Yomra",
        },
        [55] = new[] // Samsun (17 ilçe)
        {
            "19 Mayıs", "Alaçam", "Asarcık", "Atakum", "Ayvacık", "Bafra",
            "Canik", "Çarşamba", "Havza", "İlkadım", "Kavak", "Ladik",
            "Salıpazarı", "Tekkeköy", "Terme", "Vezirköprü", "Yakakent",
        },
        [21] = new[] // Diyarbakır (17 ilçe)
        {
            "Bağlar", "Bismil", "Çermik", "Çınar", "Çüngüş", "Dicle", "Eğil",
            "Ergani", "Hani", "Hazro", "Kayapınar", "Kocaköy", "Kulp",
            "Lice", "Silvan", "Sur", "Yenişehir",
        },
        [54] = new[] // Sakarya (16 ilçe)
        {
            "Adapazarı", "Akyazı", "Arifiye", "Erenler", "Ferizli", "Geyve",
            "Hendek", "Karapürçek", "Karasu", "Kaynarca", "Kocaali", "Pamukova",
            "Sapanca", "Serdivan", "Söğütlü", "Taraklı",
        },
    };
}
