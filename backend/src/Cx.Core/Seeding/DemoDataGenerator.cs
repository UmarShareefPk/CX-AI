using Cx.Core.Domain;
using Cx.Core.Scoring;
using MongoDB.Bson;

namespace Cx.Core.Seeding;

public sealed record DemoData(
    IReadOnlyList<Dealer> Dealers,
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<SurveyResponse> Responses,
    IReadOnlyList<AppUser> Users);

/// <summary>
/// Generates a reproducible demo data set. Each dealer has a quality profile (and a slow trend over the six months)
/// so scores, ranks and period-over-period changes are meaningfully different instead of uniform noise.
/// </summary>
public static class DemoDataGenerator
{
    private sealed record DealerProfile(string Id, string Name, string City, string State, string Region, double Quality, double Trend, double Weight);

    private static readonly DealerProfile[] Profiles =
    [
        new("HND-1001", "Northgate Honda", "Columbus", "OH", "Midwest", 0.90, 0.05, 1.4),
        new("HND-1002", "Riverbend Honda", "Nashville", "TN", "South", 0.62, 0.10, 1.0),
        new("HND-1003", "Summit Honda", "Denver", "CO", "West", 0.80, -0.08, 1.2),
        new("HND-1004", "Harborview Honda", "Tacoma", "WA", "West", 0.45, 0.12, 0.8),
        new("HND-1005", "Prairie Wind Honda", "Wichita", "KS", "Midwest", 0.72, 0.00, 0.7),
        new("HND-1006", "Sunbelt Honda", "Phoenix", "AZ", "West", 0.55, -0.10, 1.1),
        new("HND-1007", "Lakeshore Honda", "Chicago", "IL", "Midwest", 0.84, 0.03, 1.5),
        new("HND-1008", "Redwood Honda", "Sacramento", "CA", "West", 0.68, 0.06, 1.0),
        new("HND-1009", "Capital City Honda", "Raleigh", "NC", "South", 0.76, -0.04, 0.9),
        new("HND-1010", "Bayou Honda", "Baton Rouge", "LA", "South", 0.50, 0.00, 0.7),
    ];

    private static readonly string[] FirstNames =
    [
        "James", "Mary", "Robert", "Patricia", "John", "Jennifer", "Michael", "Linda", "David", "Elizabeth", "William", "Barbara",
        "Richard", "Susan", "Joseph", "Jessica", "Thomas", "Sarah", "Christopher", "Karen", "Daniel", "Nancy", "Matthew", "Lisa",
        "Anthony", "Betty", "Mark", "Sandra", "Steven", "Ashley", "Andrew", "Emily", "Joshua", "Donna", "Kevin", "Michelle",
        "Brian", "Carol", "George", "Amanda", "Edward", "Melissa", "Ronald", "Deborah", "Timothy", "Stephanie", "Jason", "Laura",
        "Priya", "Wei", "Carlos", "Fatima", "Kenji", "Aisha", "Mateo", "Olga", "Ahmed", "Sofia", "Raj", "Yuki",
    ];

    private static readonly string[] LastNames =
    [
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez", "Hernandez", "Lopez",
        "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson", "White",
        "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson", "Walker", "Young", "Allen", "King", "Wright", "Scott",
        "Torres", "Nguyen", "Hill", "Flores", "Green", "Adams", "Nelson", "Baker", "Hall", "Rivera", "Campbell", "Mitchell",
        "Patel", "Chen", "Kim", "Singh", "Tanaka", "Okafor", "Silva", "Kowalski", "Haddad", "Novak", "Murphy", "Fischer",
    ];

    private static readonly (string Model, double Weight)[] Models =
    [
        ("Civic", 3), ("Accord", 2.5), ("CR-V", 4), ("Pilot", 1.8), ("HR-V", 1.6), ("Odyssey", 1.2), ("Passport", 1), ("Ridgeline", 0.9), ("Prologue", 0.7),
    ];

    private static readonly string[] PasswordWords =
    [
        "Maple", "Falcon", "River", "Cedar", "Comet", "Harbor", "Summit", "Willow", "Ember", "Orchid", "Granite", "Meadow",
        "Lantern", "Pioneer", "Aurora", "Basalt", "Cobalt", "Juniper", "Marlin", "Nimbus", "Quartz", "Saffron", "Tundra", "Zephyr",
    ];

    public static DemoData Generate(int seed, DateTime nowUtc, int customerCount = 1000, int monthsBack = 6)
    {
        var rng = new Random(seed);
        var dealers = Profiles.Select(p => new Dealer
        {
            Id = p.Id, Name = p.Name, City = p.City, State = p.State, Region = p.Region, CreatedAt = nowUtc.AddYears(-3),
        }).ToList();

        var windowStart = nowUtc.AddMonths(-monthsBack);
        var windowSeconds = (nowUtc - windowStart).TotalSeconds;
        var customers = new List<Customer>(customerCount);
        var responses = new List<SurveyResponse>(customerCount);

        for (var i = 0; i < customerCount; i++)
        {
            var profile = Pick(rng, Profiles, p => p.Weight);
            var submittedAt = windowStart.AddSeconds(rng.NextDouble() * windowSeconds);
            var purchaseDate = submittedAt.Date.AddDays(-rng.Next(2, 26));
            var first = FirstNames[rng.Next(FirstNames.Length)];
            var last = LastNames[rng.Next(LastNames.Length)];
            var model = Pick(rng, Models, m => m.Weight).Model;

            var customer = new Customer
            {
                Id = ObjectId.GenerateNewId().ToString(),
                DealerId = profile.Id,
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}{rng.Next(10, 99)}@example.com".ToLowerInvariant(),
                Phone = $"({rng.Next(201, 989)}) 555-01{rng.Next(0, 100):00}",
                VehicleModel = model,
                VehicleYear = purchaseDate.Year + (purchaseDate.Month >= 9 ? 1 : 0),
                Vin = MakeVin(rng),
                PurchaseDate = purchaseDate,
            };
            customers.Add(customer);

            var progress = (submittedAt - windowStart).TotalSeconds / windowSeconds; // 0..1 across the window
            var quality = Math.Clamp(profile.Quality + profile.Trend * (progress - 0.5) * 2, 0.05, 0.98);
            responses.Add(MakeResponse(rng, customer, submittedAt, quality));
        }

        return new DemoData(dealers, customers, responses, MakeUsers(rng, dealers));
    }

    private static SurveyResponse MakeResponse(Random rng, Customer customer, DateTime submittedAt, double quality)
    {
        var pProblem = 0.03 + (1 - quality) * 0.5;
        var hasProblem = rng.NextDouble() < pProblem;
        var condition = VehicleConditionAnswer.AllGood;
        if (hasProblem)
        {
            var roll = rng.NextDouble();
            condition = roll < 0.40 ? VehicleConditionAnswer.PartMissing
                : roll < 0.85 ? VehicleConditionAnswer.Defect
                : VehicleConditionAnswer.PartMissingAndDefect;
        }

        // Latent satisfaction: dealer quality plus noise; a vehicle problem drags it down.
        var satisfaction = quality + Gaussian(rng) * 0.22 - (hasProblem ? 0.15 : 0);
        var recommend = satisfaction >= 0.72 ? RecommendAnswer.HighlyRecommend
            : satisfaction >= 0.45 ? RecommendAnswer.Recommend
            : satisfaction >= 0.25 ? RecommendAnswer.MightRecommend
            : RecommendAnswer.NotRecommend;

        return SurveyScoring.Apply(new SurveyResponse
        {
            Id = ObjectId.GenerateNewId().ToString(),
            CustomerId = customer.Id,
            DealerId = customer.DealerId,
            SubmittedAt = submittedAt,
            Recommend = recommend,
            VehicleCondition = condition,
        });
    }

    private static List<AppUser> MakeUsers(Random rng, IReadOnlyList<Dealer> dealers)
    {
        var users = new List<AppUser>
        {
            new() { Id = ObjectId.GenerateNewId().ToString(), Username = "admin", Password = MakePassword(rng), DisplayName = "Platform Administrator", Role = UserRole.Admin },
        };
        for (var i = 0; i < dealers.Count; i++)
        {
            users.Add(new AppUser
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Username = $"dealer{i + 1:00}",
                Password = MakePassword(rng),
                DisplayName = dealers[i].Name,
                Role = UserRole.Dealer,
                DealerId = dealers[i].Id,
            });
        }
        return users;
    }

    private static string MakePassword(Random rng) =>
        $"{PasswordWords[rng.Next(PasswordWords.Length)]}-{PasswordWords[rng.Next(PasswordWords.Length)]}-{rng.Next(10, 100)}";

    private static string MakeVin(Random rng)
    {
        const string chars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789"; // VINs never contain I, O or Q
        string[] wmi = ["1HG", "2HG", "5FN", "19X", "5J6"];
        var vin = new char[14];
        for (var i = 0; i < vin.Length; i++) vin[i] = chars[rng.Next(chars.Length)];
        return wmi[rng.Next(wmi.Length)] + new string(vin);
    }

    private static T Pick<T>(Random rng, IReadOnlyList<T> items, Func<T, double> weight)
    {
        var total = items.Sum(weight);
        var roll = rng.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0) return item;
        }
        return items[^1];
    }

    private static double Gaussian(Random rng) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2.0 * Math.PI * rng.NextDouble());
}
