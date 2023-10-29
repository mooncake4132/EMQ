﻿using System.Collections.Generic;

namespace EMQ.Server.Db.Imports;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
public static class Blacklists
{
    public static List<(string, string)> VndbImporterExistingSongBlacklist { get; } = new()
    {
        ("v236", "POWDER SNOW"),
        ("v12984", "Yuki no Elfin Lied"),
        ("v21901", "Ohime-sama datte XXX Shitai!!"),
        ("v17823", "Houkago Amazing Kiss"),
        ("v3", "Unmei -SADAME-"),
        ("v20", "Heart to Heart"),
        ("v238", "Until"),
        ("v273", "Eien no Negai"),
        ("v273", "Futari Dake no Ongakkai"),
        ("v273", "White Season"),
        ("v318", "Sow"),
        ("v418", "ROSE! ROSE! ROSE!"),
        ("v434", "GRIND"),
        ("v1899", "Futari"),
        ("v1950", "Tomodachi Ijou Koibito Miman"), // todo 1x1 + 2x1
        ("v2438", "Blue Twilight ~Taiyou to Tsuki ga Deau Toki~"),
        ("v2501", "Hide and seek"),
        ("v9409", "Key of Destiny"),
        // ("v10632", "Ageless Love"), // todo 2x1 + 2x1 + 2x1
        ("v11000", "Photograph Memory"),
        ("v14005", "Onaji Hohaba de, Zutto"),
        ("v15395", "Chaleur"),
        ("v15641", "Want more need less"),
        ("v31740", "Journey"),
    };

    public static List<string> EgsImporterBlacklist { get; } = new()
    {
        "https://vndb.org/v12984",
        "https://vndb.org/v1141",
        "https://vndb.org/v2002",
        "https://vndb.org/v18760",
        "https://vndb.org/v1183",
        "https://vndb.org/v28",
        "https://vndb.org/v273",
        "https://vndb.org/v1708",
        "https://vndb.org/v827",
        "https://vndb.org/v1060",
        "https://vndb.org/v2375",
        "https://vndb.org/v6846",
        "https://vndb.org/v1852",
        "https://vndb.org/v4329",
        "https://vndb.org/v2501",
        "https://vndb.org/v264",
        "https://vndb.org/v542",
        "https://vndb.org/v592",
        "https://vndb.org/v35",
        "https://vndb.org/v231",
        "https://vndb.org/v362",
        "https://vndb.org/v473",
        "https://vndb.org/v10020",
        "https://vndb.org/v22505",
        "https://vndb.org/v12849",
        "https://vndb.org/v67",
        "https://vndb.org/v68",
        "https://vndb.org/v8533",
        "https://vndb.org/v6540",
        "https://vndb.org/v575",
        "https://vndb.org/v804",
        "https://vndb.org/v1967",
        "https://vndb.org/v5939",
        "https://vndb.org/v1899",
        "https://vndb.org/v323",
        "https://vndb.org/v90",
        "https://vndb.org/v15652",
        "https://vndb.org/v2632",
        "https://vndb.org/v20424",
        "https://vndb.org/v1152",
        "https://vndb.org/v1153",
        "https://vndb.org/v1284",
        "https://vndb.org/v646",
        "https://vndb.org/v3074",
        "https://vndb.org/v2790",
        "https://vndb.org/v8435",
        "https://vndb.org/v16974",
        "https://vndb.org/v3699",
        "https://vndb.org/v916",
        "https://vndb.org/v15653",
        "https://vndb.org/v4",
        "https://vndb.org/v3859",
        "https://vndb.org/v13666",
        "https://vndb.org/v18791",
        "https://vndb.org/v15727",
        "https://vndb.org/v12831",
        "https://vndb.org/v23863",
        "https://vndb.org/v19397",
        "https://vndb.org/v17823",
        "https://vndb.org/v23740",
        "https://vndb.org/v1491",
        "https://vndb.org/v5021",
        "https://vndb.org/v421",
        "https://vndb.org/v6173",
        "https://vndb.org/v13774",
        "https://vndb.org/v17147",
        "https://vndb.org/v15658",
        "https://vndb.org/v26888",
        "https://vndb.org/v6242",
        "https://vndb.org/v1359",
        "https://vndb.org/v16032",
        "https://vndb.org/v7771",
        "https://vndb.org/v24803",
        "https://vndb.org/v4506",
        "https://vndb.org/v5834",
        "https://vndb.org/v5",
        "https://vndb.org/v38",
        "https://vndb.org/v85",
        "https://vndb.org/v180",
        "https://vndb.org/v192",
        "https://vndb.org/v200",
        "https://vndb.org/v234",
        "https://vndb.org/v266",
        "https://vndb.org/v337",
        "https://vndb.org/v369",
        "https://vndb.org/v405",
        "https://vndb.org/v515",
        "https://vndb.org/v629",
        "https://vndb.org/v862",
        "https://vndb.org/v865",
        "https://vndb.org/v1180",
        "https://vndb.org/v1280",
        "https://vndb.org/v1337",
        "https://vndb.org/v1338",
        "https://vndb.org/v1362",
        "https://vndb.org/v1399",
        "https://vndb.org/v1492",
        "https://vndb.org/v1545",
        "https://vndb.org/v1552",
        "https://vndb.org/v1646",
        "https://vndb.org/v1884",
        "https://vndb.org/v1972",
        "https://vndb.org/v2082",
        "https://vndb.org/v2205",
        "https://vndb.org/v2301",
        "https://vndb.org/v2517",
        "https://vndb.org/v2622",
        "https://vndb.org/v2654",
        "https://vndb.org/v2782",
        "https://vndb.org/v2959",
        "https://vndb.org/v3370",
        "https://vndb.org/v4308",
        "https://vndb.org/v4494",
        "https://vndb.org/v4693",
        "https://vndb.org/v4822",
        "https://vndb.org/v5097",
        "https://vndb.org/v5121",
        "https://vndb.org/v5247",
        "https://vndb.org/v5668",
        "https://vndb.org/v5957",
        "https://vndb.org/v6700",
        "https://vndb.org/v7557",
        "",
        "",
    };

    public static List<string> MusicBrainzImporterReleaseBlacklist { get; } = new()
    {
        "41c0eb47-95f4-409d-8f74-bbb85e376838", // AMBITIOUS MISSION スペシャル サウンドトラック
        "5d51da54-f097-4f94-9271-83fab6cf4ba1", // カタハネ オリジナルサウンドトラックアルバム
        "8ab6e56a-27fc-4b9e-a234-5846e6f9c26d", // ものべの スペシャルサウンドトラック
        "55b580db-3b76-447a-8478-7a9aeafd84ba", // アルカテイル
        "d34ee06f-7afd-4578-b7d1-9658de23d0e2", // アナタヲユルサナイ Mini Sound Track
        "a80e7b99-e770-4f56-b2e7-1b5d3a4e8b9b", // GYAKUTEN SAIBAN 4 MINI SOUNDTRACK CD
        "21e05ca1-9b77-4c87-9cc9-5f5340152a1b", // Remember11 Prophecy Collection Vol.1
        "9652010c-9beb-4fe0-b420-d16a15531952", // Remember11 Prophecy Collection Vol.2
        "4c647eba-f1b8-4a62-8cbd-f46cd38aa0c9", // Remember11 Prophecy Collection Vol.3
        "80bd5b53-b551-4c59-ae77-2246c6dfe23f", // Remember11 Prophecy Collection Vol.4
        "11d06123-b553-4d50-961b-d1e9b93c016e", // Remember11 Prophecy Collection Vol.5
        "1dd04b74-239d-47cc-ae5e-27c50187d2dc", // Remember11 Prophecy Collection Vol.6
        "db38f619-f4d3-4fad-9bde-e16da1e8cf3a", // 朱-Aka- ORIGINAL SOUND TRACKS
        "e812c10f-6d95-4d26-acb2-6c1889af22ef", // Steins;Gate Symphonic Material
        "644aeaa9-13c3-4b7f-98de-410d614b12a3", // クドわふたー Original SoundTrack
    };
}
