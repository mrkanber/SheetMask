namespace SheetMask.Core;

/// <summary>
/// Built-in word lists. They serve two purposes: recognising a column's content
/// (detection) and supplying replacement values (generation).
/// </summary>
public static class Pools
{
    public static readonly string[] MaleTr =
    {
        "Ahmet","Mehmet","Mustafa","Ali","Hüseyin","Hasan","İbrahim","İsmail","Osman","Yusuf",
        "Murat","Ömer","Ramazan","Halil","Süleyman","Abdullah","Mahmut","Recep","Fatih","Kadir",
        "Emre","Hakan","Burak","Serkan","Kemal","Orhan","Cem","Can","Kerem","Barış",
        "Volkan","Erkan","Tolga","Onur","Uğur","Gökhan","Selim","Tuncay","Levent","Sinan",
        "Ercan","Cengiz","Erdem","Tarık","Yasin","Eren","Arda","Berk","Deniz","Kaan",
        "Oğuz","Umut","Serdar","Tamer","Metin","Bülent","Nihat","Sami","Rıza","Ferhat",
        "Engin","Yiğit","Alper","Batuhan","Furkan","Enes","Emir","Mert","Çağlar","Doğan",
        "Erhan","Fikret","Güven","İlker","Kenan","Necati","Okan","Rasim","Sefa","Taner",
        "Turgut","Vedat","Yavuz","Zafer","Anıl","Bora","Cihan","Egemen","Halit","İlhan",
        "Koray","Melih","Nurettin","Özgür","Polat","Sedat","Şahin","Tayfun","Ufuk","Yücel"
    };

    public static readonly string[] FemaleTr =
    {
        "Fatma","Ayşe","Emine","Hatice","Zeynep","Elif","Meryem","Şerife","Zehra","Sultan",
        "Hanife","Merve","Havva","Zeliha","Esra","Fadime","Özlem","Hacer","Melek","Yasemin",
        "Hülya","Leyla","Büşra","Derya","Gül","Sevgi","Songül","Dilek","Tuğba","Gamze",
        "Seda","Ebru","Pınar","Sibel","Aslı","Burcu","Ceren","Damla","Ece","Gizem",
        "İrem","Kübra","Nur","Selin","Sena","Şule","Tuba","Yeliz","Zerrin","Aylin",
        "Banu","Canan","Didem","Esin","Filiz","Gönül","Hande","Işıl","Jale","Kader",
        "Lale","Melike","Nazlı","Oya","Pelin","Rabia","Serap","Tülay","Ümran","Vildan",
        "Yeşim","Zuhal","Bahar","Cansu","Defne","Eda","Feride","Gülay","Hilal","İpek",
        "Arzu","Belgin","Çiğdem","Duygu","Emel","Figen","Gülşen","Hale","Nergis","Nilay",
        "Meltem","Neslihan","Nihan","Sevda","Sevim","Şeyma","Tülin","Ülkü","Yağmur","Zübeyde"
    };

    public static readonly string[] SurnameTr =
    {
        "Yılmaz","Kaya","Demir","Şahin","Çelik","Yıldız","Yıldırım","Öztürk","Aydın","Özdemir",
        "Arslan","Doğan","Kılıç","Aslan","Çetin","Kara","Koç","Kurt","Özkan","Şimşek",
        "Polat","Özcan","Korkmaz","Çakır","Erdoğan","Yavuz","Can","Acar","Şen","Aktaş",
        "Güler","Yalçın","Güneş","Bozkurt","Bulut","Keskin","Ünal","Turan","Gül","Özer",
        "Işık","Kaplan","Avcı","Sarı","Tekin","Taş","Köse","Yüksel","Ateş","Aksoy",
        "Ekinci","Uçar","Karaca","Akın","Bayram","Karataş","Tuncer","Kurtuluş","Altun","Erdem",
        "Uysal","Duman","Sezer","Akgül","Toprak","Coşkun","Kocabaş","Tunç","Akbulut","Başaran",
        "Ceylan","Durmaz","Eren","Gündüz","Kahraman","Karakaya","Oral","Önal","Sevim","Soylu",
        "Tan","Tatlı","Uğurlu","Vural","Yazıcı","Zengin","Akyüz","Bilgin","Cansız","Dinç",
        "Ergin","Gökçe","Gürbüz","İnce","Kalkan","Karabulut","Kocaman","Mutlu","Oğuz","Orhan",
        "Özbek","Pehlivan","Sağlam","Selvi","Sönmez","Tok","Topal","Türkmen","Uzun","Yaman",
        "Yücel","Akdağ","Albayrak","Aydoğan","Balcı","Bayrak","Çiftçi","Demirci","Dursun","Ekici",
        "Esen","Gezer","Göksu","Güven","Kandemir","Karadeniz","Kaygusuz","Kırmızı","Koyuncu","Kuru",
        "Metin","Nas","Okur","Ozan","Parlak","Savaş","Seçkin","Şeker","Taşkın","Temiz",
        "Tosun","Uslu","Yaşar","Yeşil","Yiğit","Zorlu","Akman","Alkan","Bozdağ","Cömert"
    };

    public static readonly string[] MaleEn =
    {
        "James","John","Robert","Michael","William","David","Richard","Joseph","Thomas","Charles",
        "Daniel","Matthew","Anthony","Mark","Steven","Paul","Andrew","Joshua","Kevin","Brian",
        "George","Edward","Ronald","Timothy","Jason","Jeffrey","Ryan","Jacob","Gary","Eric"
    };

    public static readonly string[] FemaleEn =
    {
        "Mary","Patricia","Jennifer","Linda","Elizabeth","Barbara","Susan","Jessica","Sarah","Karen",
        "Nancy","Lisa","Betty","Margaret","Sandra","Ashley","Emily","Donna","Michelle","Carol",
        "Amanda","Melissa","Deborah","Stephanie","Rebecca","Laura","Sharon","Cynthia","Kathleen","Amy"
    };

    public static readonly string[] SurnameEn =
    {
        "Smith","Johnson","Williams","Brown","Jones","Garcia","Miller","Davis","Rodriguez","Martinez",
        "Hernandez","Lopez","Wilson","Anderson","Taylor","Thomas","Moore","Jackson","Martin","Lee",
        "Thompson","White","Harris","Clark","Lewis","Robinson","Walker","Young","Allen","King"
    };

    public static readonly string[] CitiesTr =
    {
        "Adana","Adıyaman","Afyonkarahisar","Ağrı","Amasya","Ankara","Antalya","Artvin","Aydın","Balıkesir",
        "Bilecik","Bingöl","Bitlis","Bolu","Burdur","Bursa","Çanakkale","Çankırı","Çorum","Denizli",
        "Diyarbakır","Edirne","Elazığ","Erzincan","Erzurum","Eskişehir","Gaziantep","Giresun","Gümüşhane","Hakkari",
        "Hatay","Isparta","Mersin","İstanbul","İzmir","Kars","Kastamonu","Kayseri","Kırklareli","Kırşehir",
        "Kocaeli","Konya","Kütahya","Malatya","Manisa","Kahramanmaraş","Mardin","Muğla","Muş","Nevşehir",
        "Niğde","Ordu","Rize","Sakarya","Samsun","Siirt","Sinop","Sivas","Tekirdağ","Tokat",
        "Trabzon","Tunceli","Şanlıurfa","Uşak","Van","Yozgat","Zonguldak","Aksaray","Bayburt","Karaman",
        "Kırıkkale","Batman","Şırnak","Bartın","Ardahan","Iğdır","Yalova","Karabük","Kilis","Osmaniye","Düzce"
    };

    /// <summary>Extra place names used as replacements once province names run out.</summary>
    public static readonly string[] TownsTr =
    {
        "Alanya","Bodrum","Fethiye","Marmaris","Çeşme","Kuşadası","Didim","Ayvalık","Bandırma","Gemlik",
        "İnegöl","Tarsus","Ceyhan","Kozan","Elbistan","Ereğli","Akşehir","Polatlı","Beypazarı","Safranbolu",
        "Merzifon","Ünye","Fatsa","Bafra","Çarşamba","Akçakoca","Silivri","Çorlu","Lüleburgaz","Keşan",
        "Gelibolu","Biga","Edremit","Burhaniye","Soma","Akhisar","Salihli","Turgutlu","Ödemiş","Tire",
        "Bergama","Nazilli","Söke","Milas","Dalaman","Kemer","Manavgat","Serik","Kaş","Eğirdir",
        "Yalvaç","Seydişehir","Karapınar","Ürgüp","Avanos","Develi","Kozaklı","Sorgun","Suşehri","Zara"
    };

    public static readonly string[] CountriesTr =
    {
        "Türkiye","Almanya","Fransa","İngiltere","İtalya","İspanya","Hollanda","Belçika","Avusturya","İsviçre",
        "İsveç","Norveç","Danimarka","Finlandiya","Polonya","Çekya","Macaristan","Romanya","Bulgaristan","Yunanistan",
        "Portekiz","İrlanda","Rusya","Ukrayna","Gürcistan","Azerbaycan","Kazakistan","Özbekistan","İran","Irak",
        "Suriye","Mısır","Fas","Tunus","Cezayir","Libya","Suudi Arabistan","Katar","Kuveyt","Birleşik Arap Emirlikleri",
        "Ürdün","Lübnan","İsrail","Pakistan","Hindistan","Çin","Japonya","Güney Kore","Endonezya","Malezya",
        "Tayland","Vietnam","Avustralya","Yeni Zelanda","Kanada","Amerika Birleşik Devletleri","Meksika","Brezilya","Arjantin","Şili",
        "Kolombiya","Peru","Güney Afrika","Nijerya","Kenya","Etiyopya","Sırbistan","Hırvatistan","Bosna Hersek","Arnavutluk",
        "Kuzey Makedonya","Karadağ","Slovenya","Slovakya","Litvanya","Letonya","Estonya","Moldova","Belarus","Kıbrıs"
    };

    public static readonly string[] CountriesEn =
    {
        "Turkey","Germany","France","United Kingdom","Italy","Spain","Netherlands","Belgium","Austria","Switzerland",
        "Sweden","Norway","Denmark","Finland","Poland","Czechia","Hungary","Romania","Bulgaria","Greece",
        "Portugal","Ireland","Russia","Ukraine","Georgia","Azerbaijan","Kazakhstan","Uzbekistan","Iran","Iraq",
        "Syria","Egypt","Morocco","Tunisia","Algeria","Libya","Saudi Arabia","Qatar","Kuwait","United Arab Emirates",
        "Jordan","Lebanon","Israel","Pakistan","India","China","Japan","South Korea","Indonesia","Malaysia",
        "Thailand","Vietnam","Australia","New Zealand","Canada","United States","Mexico","Brazil","Argentina","Chile",
        "Colombia","Peru","South Africa","Nigeria","Kenya","Ethiopia","Serbia","Croatia","Bosnia and Herzegovina","Albania",
        "North Macedonia","Montenegro","Slovenia","Slovakia","Lithuania","Latvia","Estonia","Moldova","Belarus","Cyprus","Türkiye","USA","UK"
    };

    public static readonly string[] Streets =
    {
        "Atatürk","Cumhuriyet","İstiklal","Gazi","Fatih","Mimar Sinan","Yunus Emre","Mevlana","Barış","Çamlık",
        "Gül","Lale","Menekşe","Papatya","Akasya","Ihlamur","Çınar","Defne","Zeytin","Pınar",
        "Bahar","Güneş","Yıldız","Deniz","Orman","Vadi","Tepe","Bağlar","Kavak","Söğüt",
        "Kardelen","Sümbül","Nergis","Erguvan","Manolya","Gelincik","Leylak","Ardıç","Meşe","Sedir"
    };

    public static readonly string[] PublicMailDomains =
    {
        "gmail.com","hotmail.com","outlook.com","yahoo.com","icloud.com","yandex.com","live.com",
        "msn.com","hotmail.com.tr","yandex.com.tr","mail.ru","protonmail.com","proton.me","aol.com"
    };

    // ---------------------------------------------------------------- lookups

    private static HashSet<string> Set(params string[][] lists) =>
        new(lists.SelectMany(l => l).Select(Text.Norm));

    public static readonly HashSet<string> FemaleKeys = Set(FemaleTr, FemaleEn);
    public static readonly HashSet<string> EnglishKeys = Set(MaleEn, FemaleEn, SurnameEn);
    public static readonly HashSet<string> FirstNameKeys = Set(MaleTr, FemaleTr, MaleEn, FemaleEn);
    public static readonly HashSet<string> SurnameKeys = Set(SurnameTr, SurnameEn);
    public static readonly HashSet<string> CityKeys = Set(CitiesTr, TownsTr);
    public static readonly HashSet<string> CountryKeys = Set(CountriesTr, CountriesEn);
    public static readonly HashSet<string> CountryEnKeys = Set(CountriesEn);
    public static readonly HashSet<string> CountryTrKeys = Set(CountriesTr);
}
