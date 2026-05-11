using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Course> Courses =
    [
        new()
        {
            Id           = "demo-course-1",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO105",
            Title        = "Human Biology",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-2",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO106",
            Title        = "Ecology from an Urban Perspective",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-3",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO106LAB",
            Title        = "Ecology from an Urban Perspective LAB",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-4",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO111",
            Title        = "Introductory Biology I",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-5",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO111LAB",
            Title        = "Introductory Biology I LAB",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-6",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO112",
            Title        = "Introductory Biology II",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-7",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO112LAB",
            Title        = "Introductory Biology II LAB",
            IsActive     = true,
            Level        = "100"
        },
        new()
        {
            Id           = "demo-course-8",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO201",
            Title        = "Cell Biochemistry/Metabolism",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-9",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO201LAB",
            Title        = "Cell Biochemistry/Metabolism LAB",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-10",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO202",
            Title        = "Cell Signaling/Gene Regulation",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-11",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO202LAB",
            Title        = "Cell Signaling/Gene Regulation LAB",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-12",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO210",
            Title        = "Introduction to Ecology",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-13",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO210LAB",
            Title        = "Introduction to Ecology LAB",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-14",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO219",
            Title        = "Biogeography",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-15",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO220",
            Title        = "Genetics",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-16",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO220LAB",
            Title        = "Genetics LAB",
            IsActive     = true,
            Level        = "200"
        },
        new()
        {
            Id           = "demo-course-17",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO301",
            Title        = "Anatomy and Physiology of Invertebrates",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-18",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO301LAB",
            Title        = "Anatomy and Physiology of Invertebrates LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-19",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO305",
            Title        = "Structural and Functional Anatomy of Vertebrates",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-20",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO305LAB",
            Title        = "Structural and Functional Anatomy of Vertebrates LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-21",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO306",
            Title        = "Vertebrate Organ Systems",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-22",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO306LAB",
            Title        = "Vertebrate Organ Systems LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-23",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO307",
            Title        = "Anatomy and Diversity of Plants",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-24",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO307LAB",
            Title        = "Anatomy and Diversity of Plants LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-25",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO308",
            Title        = "Plant Physiology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-26",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO308LAB",
            Title        = "Plant Physiology LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-27",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO309",
            Title        = "Microbiology I",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-28",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO309LAB",
            Title        = "Microbiology I LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-29",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO310",
            Title        = "Conservation Biology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-30",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO312",
            Title        = "Developmental Biology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-31",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO319",
            Title        = "Swamps and Bogs",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-32",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO320",
            Title        = "Biochemistry",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-33",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO330",
            Title        = "Plants and Animals of BC",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-34",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO330LAB",
            Title        = "Plants and Animals of BC LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-35",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO333",
            Title        = "Bioinformatics I",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-36",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO335",
            Title        = "Freshwater Ecology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-37",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO335LAB",
            Title        = "Freshwater Ecology LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-38",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO340",
            Title        = "Population and Community Ecology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-39",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO340LAB",
            Title        = "Population and Community Ecology LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-40",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO350",
            Title        = "Medical Genetics",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-41",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO357",
            Title        = "Conservation GIS",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-42",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO360",
            Title        = "Insect Biology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-43",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO360LAB",
            Title        = "Insect Biology LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-44",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO370",
            Title        = "Introduction to Mycology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-45",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO370LAB",
            Title        = "Introduction to Mycology LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-46",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO380",
            Title        = "Ornithology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-47",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO380LAB",
            Title        = "Ornithology LAB",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-48",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO383",
            Title        = "Human Physiology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-49",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO385",
            Title        = "Neurobiology",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-50",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO390",
            Title        = "Animal Behaviour",
            IsActive     = true,
            Level        = "300"
        },
        new()
        {
            Id           = "demo-course-51",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO401",
            Title        = "Molecular Biology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-52",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO403",
            Title        = "Molecular Techniques I",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-53",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO406",
            Title        = "Advanced Genetics",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-54",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO407",
            Title        = "Applied Biotechnology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-55",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO408",
            Title        = "Directed Studies in Biology I",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-56",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO409",
            Title        = "Directed Studies in Biology II",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-57",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO410",
            Title        = "Plant Ecology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-58",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO410LAB",
            Title        = "Plant Ecology LAB",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-59",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO412",
            Title        = "Advanced Metabolism",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-60",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO414",
            Title        = "Genomics",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-61",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO415",
            Title        = "Cancer Biology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-62",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO416",
            Title        = "Evolution",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-63",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO418",
            Title        = "Ethnobotany",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-64",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO418LAB",
            Title        = "Ethnobotany LAB",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-65",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO419",
            Title        = "Paleoecology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-66",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO420II",
            Title        = "Science Communications",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-67",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO420KK",
            Title        = "Metabolomics",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-68",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO420LL",
            Title        = "Natural Product Biochemistry",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-69",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO425",
            Title        = "Introductory Medical Microbiology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-70",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO425LAB",
            Title        = "Introductory Medical Microbiology LAB",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-71",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO426",
            Title        = "Environmental Microbiology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-72",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO426LAB",
            Title        = "Enviromental Microbiology LAB",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-73",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO427",
            Title        = "Plants and Drugs",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-74",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO430",
            Title        = "Forest Ecology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-75",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO433",
            Title        = "Bioinformatics II",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-76",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO442",
            Title        = "Biological Field School",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-77",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO448",
            Title        = "Immunology",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-78",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO477",
            Title        = "Traditional Ecological Knowledge",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-79",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO496",
            Title        = "Advanced Biological Topics",
            IsActive     = true,
            Level        = "400"
        },
        new()
        {
            Id           = "demo-course-80",
            SubjectId    = "demo-subj-1",
            CalendarCode = "BIO499",
            Title        = "Honours Research Thesis",
            IsActive     = true,
            Level        = "400"
        }
    ];
}
