namespace JobRadar.Services;

public static class SkillExtractor
{
    private static readonly Dictionary<string, string[]> SkillKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Languages
        ["C#"] = ["c#", "csharp", "c sharp"],
        ["Python"] = ["python", "django", "flask", "fastapi"],
        ["JavaScript"] = ["javascript", "js", "node.js", "nodejs"],
        ["TypeScript"] = ["typescript", "ts"],
        ["Java"] = ["java", "spring", "spring boot"],
        ["Go"] = ["golang", " go "],
        ["Rust"] = ["rust"],
        ["PHP"] = ["php", "laravel", "symfony"],
        ["Ruby"] = ["ruby", "rails", "ruby on rails"],
        ["Swift"] = ["swift", "swiftui"],
        ["Kotlin"] = ["kotlin"],
        ["Scala"] = ["scala"],
        ["R"] = [" r ", "rstudio", "tidyverse"],

        // Frontend
        ["React"] = ["react", "reactjs", "react.js"],
        ["Angular"] = ["angular", "angularjs"],
        ["Vue"] = ["vue", "vuejs", "vue.js"],
        ["Blazor"] = ["blazor"],
        ["HTML/CSS"] = ["html", "css", "scss", "sass"],
        ["Next.js"] = ["next.js", "nextjs"],

        // Backend / Frameworks
        [".NET"] = [".net", "asp.net", "dotnet"],
        ["Spring"] = ["spring boot", "spring framework"],
        ["Express"] = ["express.js", "expressjs"],
        ["FastAPI"] = ["fastapi"],

        // Databases
        ["SQL"] = ["sql", "t-sql", "pl/sql"],
        ["SQL Server"] = ["sql server", "mssql"],
        ["PostgreSQL"] = ["postgresql", "postgres"],
        ["MySQL"] = ["mysql"],
        ["MongoDB"] = ["mongodb", "mongo"],
        ["Redis"] = ["redis"],
        ["Elasticsearch"] = ["elasticsearch", "elastic"],
        ["Cosmos DB"] = ["cosmos db", "cosmosdb"],

        // Cloud
        ["AWS"] = ["aws", "amazon web services", "ec2", "s3", "lambda"],
        ["Azure"] = ["azure", "microsoft azure"],
        ["GCP"] = ["gcp", "google cloud", "google cloud platform"],

        // DevOps
        ["Docker"] = ["docker", "containerization"],
        ["Kubernetes"] = ["kubernetes", "k8s"],
        ["CI/CD"] = ["ci/cd", "cicd", "github actions", "gitlab ci", "jenkins", "azure devops"],
        ["Terraform"] = ["terraform", "iac"],

        // Data / ML
        ["Machine Learning"] = ["machine learning", "ml", "deep learning"],
        ["TensorFlow"] = ["tensorflow"],
        ["PyTorch"] = ["pytorch"],
        ["Pandas"] = ["pandas", "numpy"],
        ["Power BI"] = ["power bi", "powerbi"],
        ["Tableau"] = ["tableau"],

        // Other
        ["Git"] = ["git", "github", "gitlab", "bitbucket"],
        ["REST API"] = ["rest api", "restful", "api development"],
        ["GraphQL"] = ["graphql"],
        ["Microservices"] = ["microservices", "microservice"],
        ["Agile"] = ["agile", "scrum", "kanban"],
        ["Linux"] = ["linux", "unix", "bash"],
    };

    private static readonly Dictionary<string, string> ExperiencePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Entry"] = "0-2 years|entry.level|junior|new grad|internship|associate",
        ["Mid"] = "2-5 years|3-5 years|mid.level|intermediate",
        ["Senior"] = "5\\+ years|senior|sr\\.",
        ["Lead"] = "lead|principal|staff|architect|manager",
    };

    public static List<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (skill, keywords) in SkillKeywords)
        {
            foreach (var kw in keywords)
            {
                if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(skill);
                    break;
                }
            }
        }

        return [.. found.OrderBy(s => s)];
    }

    public static string DetectExperienceLevel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Unknown";

        foreach (var (level, pattern) in ExperiencePatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return level;
        }

        return "Mid"; // default
    }

    public static bool DetectRemote(string? text) =>
        text != null && System.Text.RegularExpressions.Regex.IsMatch(text,
            @"\bremote\b|\bwork from home\b|\bwfh\b|\bhybrid\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
