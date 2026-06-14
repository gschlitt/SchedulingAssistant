using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Instructor> Instructors =
    [
        new()
        {
            Id          = "demo-inst-1",
            FirstName   = "Dante",
            LastName    = "Bahringer",
            Initials    = "DB",
            IsActive    = false,
            Email       = "dante.bahringer@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-2",
            FirstName   = "Ruthe",
            LastName    = "Botsford",
            Initials    = "RB",
            IsActive    = true,
            Email       = "ruthe.botsford@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-3",
            FirstName   = "Grover",
            LastName    = "Brown",
            Initials    = "GB",
            IsActive    = true,
            Email       = "grover.brown@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-4",
            FirstName   = "Jazmyn",
            LastName    = "Carroll",
            Initials    = "JC",
            IsActive    = false,
            Email       = "jazmyn.carroll@udelphi.edu",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-5",
            FirstName   = "Freida",
            LastName    = "Cremin",
            Initials    = "FC",
            IsActive    = true,
            Email       = "freida.cremin@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-6",
            FirstName   = "Santa",
            LastName    = "Crooks",
            Initials    = "SC",
            IsActive    = true,
            Email       = "santa.crooks@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-7",
            FirstName   = "Sammy",
            LastName    = "Donnelly",
            Initials    = "PP",
            IsActive    = true,
            Email       = "sammy.donnelly@udelphi.edu",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-8",
            FirstName   = "Paolo",
            LastName    = "Dooley",
            Initials    = "PD",
            IsActive    = true,
            Email       = "paolo.dooley@udelphi.edu",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-9",
            FirstName   = "Abbigail",
            LastName    = "Douglas",
            Initials    = "AD",
            IsActive    = true,
            Email       = "abbigail.douglas@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-10",
            FirstName   = "Johann",
            LastName    = "Gleason",
            Initials    = "JG",
            IsActive    = true,
            Email       = "johann.gleason@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-11",
            FirstName   = "Rhea",
            LastName    = "Grant",
            Initials    = "RG",
            IsActive    = false,
            Email       = "rhea.grant@udelphi.edu"
        },
        new()
        {
            Id          = "demo-inst-12",
            FirstName   = "Charles",
            LastName    = "Gulgowski",
            Initials    = "CG",
            IsActive    = true,
            Email       = "charles.gulgowski@udelphi.edu",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-13",
            FirstName   = "Brielle",
            LastName    = "Hartmann",
            Initials    = "BH",
            IsActive    = true,
            Email       = "brielle.hartmann@udelphi.edu",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-14",
            FirstName   = "Mac",
            LastName    = "Herzog",
            Initials    = "MH",
            IsActive    = true,
            Email       = "mac.herzog@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-15",
            FirstName   = "Micah",
            LastName    = "Hills",
            Initials    = "MH",
            IsActive    = true,
            Email       = "micah.hills@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-16",
            FirstName   = "Dax",
            LastName    = "Hyatt",
            Initials    = "DH",
            IsActive    = true,
            Email       = "dax.hyatt@udelphi.edu",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-17",
            FirstName   = "Monserrat",
            LastName    = "Jenkins",
            Initials    = "MJ",
            IsActive    = true,
            Email       = "monserrat.jenkins@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-18",
            FirstName   = "Ramon",
            LastName    = "Jones",
            Initials    = "RJ",
            IsActive    = true,
            Email       = "ramon.jones@udelphi.edu"
        },
        new()
        {
            Id          = "demo-inst-19",
            FirstName   = "Louie",
            LastName    = "Konopelski",
            Initials    = "LK",
            IsActive    = true,
            Email       = "louie.konopelski@udelphi.edu",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-20",
            FirstName   = "Hilton",
            LastName    = "Kreiger",
            Initials    = "HK",
            IsActive    = true,
            Email       = "hilton.kreiger@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-21",
            FirstName   = "Delores",
            LastName    = "Kshlerin",
            Initials    = "DK",
            IsActive    = false,
            Email       = "delores.kshlerin@udelphi.edu"
        },
        new()
        {
            Id          = "demo-inst-22",
            FirstName   = "Kasey",
            LastName    = "Little",
            Initials    = "KL",
            IsActive    = true,
            Email       = "kasey.little@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-23",
            FirstName   = "Shaina",
            LastName    = "Mueller",
            Initials    = "SM",
            IsActive    = true,
            Email       = "shaina.mueller@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-24",
            FirstName   = "Katlynn",
            LastName    = "Orn",
            Initials    = "KO",
            IsActive    = false,
            Email       = "katlynn.orn@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-25",
            FirstName   = "Deondre",
            LastName    = "Padberg",
            Initials    = "DP",
            IsActive    = true,
            Email       = "deondre.padberg@udelphi.edu",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-26",
            FirstName   = "Jalyn",
            LastName    = "Padberg",
            Initials    = "JP",
            IsActive    = true,
            Email       = "jalyn.padberg@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-27",
            FirstName   = "Gudrun",
            LastName    = "Prohaska",
            Initials    = "GP",
            IsActive    = true,
            Email       = "gudrun.prohaska@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-28",
            FirstName   = "Jaylan",
            LastName    = "Prosacco",
            Initials    = "JP",
            IsActive    = true,
            Email       = "jaylan.prosacco@udelphi.edu",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-29",
            FirstName   = "Quentin",
            LastName    = "Ruecker",
            Initials    = "QR",
            IsActive    = true,
            Email       = "quentin.ruecker@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-30",
            FirstName   = "Valentine",
            LastName    = "Sauer",
            Initials    = "VS",
            IsActive    = true,
            Email       = "valentine.sauer@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-31",
            FirstName   = "Kaelyn",
            LastName    = "Sipes",
            Initials    = "KS",
            IsActive    = true,
            Email       = "kaelyn.sipes@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-32",
            FirstName   = "Curt",
            LastName    = "Towne",
            Initials    = "CT",
            IsActive    = true,
            Email       = "curt.towne@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-33",
            FirstName   = "Rita",
            LastName    = "Ullrich",
            Initials    = "RU",
            IsActive    = true,
            Email       = "rita.ullrich@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-34",
            FirstName   = "Adonis",
            LastName    = "VonRueden",
            Initials    = "AV",
            IsActive    = true,
            Email       = "adonis.vonrueden@udelphi.edu",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-35",
            FirstName   = "Elias",
            LastName    = "Wehner",
            Initials    = "EW",
            IsActive    = true,
            Email       = "elias.wehner@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-36",
            FirstName   = "Misael",
            LastName    = "Weissnat",
            Initials    = "MW",
            IsActive    = true,
            Email       = "misael.weissnat@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-37",
            FirstName   = "Antonia",
            LastName    = "White",
            Initials    = "AW",
            IsActive    = true,
            Email       = "antonia.white@udelphi.edu",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-38",
            FirstName   = "Shanny",
            LastName    = "Wilkinson",
            Initials    = "SW",
            IsActive    = true,
            Email       = "shanny.wilkinson@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        },
        new()
        {
            Id          = "demo-inst-39",
            FirstName   = "Aurore",
            LastName    = "Yost",
            Initials    = "AY",
            IsActive    = true,
            Email       = "aurore.yost@udelphi.edu",
            StaffTypeId = "demo-staff-1"
        }
    ];
}
