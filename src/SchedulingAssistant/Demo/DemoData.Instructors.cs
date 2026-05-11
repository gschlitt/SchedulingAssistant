using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Instructor> Instructors =
    [
        new()
        {
            Id          = "demo-inst-1",
            FirstName   = "Avril",
            LastName    = "Alfred",
            Initials    = "AA",
            IsActive    = true,
            Email       = "avril.alfred@ufv.ca",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-2",
            FirstName   = "Jillian",
            LastName    = "Bainard",
            Initials    = "JillB",
            IsActive    = true,
            Email       = "jillian.bainard@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-3",
            FirstName   = "Jennifer",
            LastName    = "Barrett",
            Initials    = "JennB",
            IsActive    = true,
            Email       = "jennifer.barrett@ufv.ca",
            StaffTypeId = "demo-staff-6"
        },
        new()
        {
            Id          = "demo-inst-4",
            FirstName   = "Angela",
            LastName    = "Bedard",
            Initials    = "AB",
            IsActive    = true,
            Email       = "angela.bedard@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-5",
            FirstName   = "James",
            LastName    = "Bedard",
            Initials    = "JB",
            IsActive    = true,
            Email       = "james.bedard@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-6",
            FirstName   = "Nathan",
            LastName    = "Bialas",
            Initials    = "NB",
            IsActive    = true,
            Email       = "nathan.bialas@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-7",
            FirstName   = "Carin",
            LastName    = "Bondar",
            Initials    = "CB",
            IsActive    = true,
            Email       = "carin.bondar@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-8",
            FirstName   = "Riley",
            LastName    = "Brown",
            Initials    = "RB",
            IsActive    = true,
            Email       = "ariel.brown@ufv.ca",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-9",
            FirstName   = "Donna",
            LastName    = "Cullon",
            Initials    = "DC",
            IsActive    = true,
            Email       = "donna.cullon@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-10",
            FirstName   = "Shawna",
            LastName    = "Dyck",
            Initials    = "SD",
            IsActive    = true,
            Email       = "shawna.dyck@ufv.ca",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-11",
            FirstName   = "Yvonne",
            LastName    = "Dzal",
            Initials    = "YD",
            IsActive    = true,
            Email       = "yvonne.dzal@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-12",
            FirstName   = "Lauren",
            LastName    = "Erland",
            Initials    = "LE",
            IsActive    = true,
            Email       = "lauren.erland@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-13",
            FirstName   = "David",
            LastName    = "Fenske",
            Initials    = "DF",
            IsActive    = true,
            Email       = "david.fenske@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-14",
            FirstName   = "Sandra",
            LastName    = "Gillespie",
            Initials    = "SanG",
            IsActive    = true,
            Email       = "sandra.gillespie@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-15",
            FirstName   = "Sharon",
            LastName    = "Gillies",
            Initials    = "SG",
            IsActive    = true,
            Email       = "sharon.gillies@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-16",
            FirstName   = "Harley",
            LastName    = "Gordon",
            Initials    = "HG",
            IsActive    = true,
            Email       = "harley.gordon@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-17",
            FirstName   = "Shannon",
            LastName    = "Guichon",
            Initials    = "ShanG",
            IsActive    = true,
            Email       = "shannon.guichon@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-18",
            FirstName   = "Brenna",
            LastName    = "Hay",
            Initials    = "BH",
            IsActive    = true,
            Email       = "brenna.hay@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-19",
            FirstName   = "Alida",
            LastName    = "Janmaat",
            Initials    = "AJ",
            IsActive    = true,
            Email       = "alida.janmaat@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-20",
            FirstName   = "Valentina",
            LastName    = "Jovanovic",
            Initials    = "VJ",
            IsActive    = false,
            Email       = "valentina.jovanovic@ufv.ca",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-21",
            FirstName   = "Justin",
            LastName    = "Lee",
            Initials    = "JL",
            IsActive    = true,
            Email       = "justin.lee@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-22",
            FirstName   = "Carlos",
            LastName    = "Leon",
            Initials    = "CL",
            IsActive    = false,
            Email       = "carlos.leon@ufv.ca",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-23",
            FirstName   = "Dina",
            LastName    = "Navon",
            Initials    = "DN",
            IsActive    = true,
            Email       = "dina.navon@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-24",
            FirstName   = "Bassam",
            LastName    = "Nyaeme",
            Initials    = "BN",
            IsActive    = true,
            Email       = "bassam.nyaeme@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-25",
            FirstName   = "Shayne",
            LastName    = "Oberhoffner",
            Initials    = "OS",
            IsActive    = true,
            Email       = "shayne.oberhoffner@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-26",
            FirstName   = "Dilan",
            LastName    = "Praat",
            Initials    = "DilP",
            IsActive    = true,
            Email       = "dilan.praat@ufv.ca",
            StaffTypeId = "demo-staff-7"
        },
        new()
        {
            Id          = "demo-inst-27",
            FirstName   = "Daylan",
            LastName    = "Pritchard",
            Initials    = "DP",
            IsActive    = true,
            Email       = "daylan.pritchard@ufv.ca",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-28",
            FirstName   = "Alan",
            LastName    = "Reid",
            Initials    = "AR",
            IsActive    = true,
            Email       = "alan.reid@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-29",
            FirstName   = "Fabiola",
            LastName    = "Rojas",
            Initials    = "FabR",
            IsActive    = true,
            Email       = "fabiola.rojas@ufv.ca",
            StaffTypeId = "demo-staff-5"
        },
        new()
        {
            Id          = "demo-inst-30",
            FirstName   = "Greg",
            LastName    = "Schmaltz",
            Initials    = "GS",
            IsActive    = true,
            Email       = "gregory.schmaltz@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-31",
            FirstName   = "Jane Jae-Kyung",
            LastName    = "Shin",
            Initials    = "JS",
            IsActive    = true,
            Email       = "jane.shin@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-32",
            FirstName   = "Carrie",
            LastName    = "Sim",
            Initials    = "SC",
            IsActive    = true,
            Email       = "carrie.sim@ufv.ca",
            StaffTypeId = "demo-staff-2"
        },
        new()
        {
            Id          = "demo-inst-33",
            FirstName   = "Lindsay",
            LastName    = "Spielman",
            Initials    = "LS",
            IsActive    = false,
            Email       = "lindsay.spielman@ufv.ca",
            StaffTypeId = "demo-staff-3"
        },
        new()
        {
            Id          = "demo-inst-34",
            FirstName   = "Anthony",
            LastName    = "Stea",
            Initials    = "AS",
            IsActive    = false,
            Email       = "anthony.stea@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-35",
            FirstName   = "Mitra",
            LastName    = "Tabatabaee",
            Initials    = "MitT",
            IsActive    = true,
            Email       = "mitra.tabatabaee@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-36",
            FirstName   = "Marina",
            LastName    = "Tourlakis",
            Initials    = "MarT",
            IsActive    = true,
            Email       = "marina.tourlakis@ufv.ca",
            StaffTypeId = "demo-staff-4"
        },
        new()
        {
            Id          = "demo-inst-37",
            FirstName   = "Natallia",
            LastName    = "Varankovich",
            Initials    = "NV",
            IsActive    = true,
            Email       = "natallia.varankovich@ufv.ca",
            StaffTypeId = "demo-staff-6"
        },
        new()
        {
            Id          = "demo-inst-38",
            FirstName   = "Debbie",
            LastName    = "Wheeler",
            Initials    = "DW",
            IsActive    = true,
            Email       = "debbie.wheeler@ufv.ca",
            StaffTypeId = "demo-staff-6"
        },
        new()
        {
            Id          = "demo-inst-39",
            FirstName   = "Dylan",
            LastName    = "Ziegler",
            Initials    = "DZ",
            IsActive    = false,
            Email       = "dylan.ziegler@ufv.ca",
            StaffTypeId = "demo-staff-2"
        }
    ];
}
