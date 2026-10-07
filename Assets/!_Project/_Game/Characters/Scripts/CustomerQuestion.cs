// What a lost shopper says, and what they say when you get them there. Shoppers ask the way
// people really do: by name ("I'm looking for Pipisi Zero"), by aisle ("which aisle is the
// tuna in?", "where's aisle 5?"), by department ("where's the bakery?"), or because the shelf in
// front of them is empty ("is there any more Kafé coffee?").
public static class CustomerQuestion
{
    public enum Kind
    {
        Product,      // walked to the facing
        WhichAisle,   // walked to the facing; they learn the aisle on the way
        Aisle,        // walked to the aisle: anywhere in it will do
        Department,   // walked to the department, likewise
        Missing,      // the facing they found was empty: walked to one that isn't
    }

    // How often each kind is asked when nothing is missing. Departments have no number, so a
    // product sold round the edge of the shop is asked for by department instead of aisle.
    public const float ProductChance = 0.45f;
    public const float WhichAisleChance = 0.2f;
    public const float PlaceChance = 0.25f;     // the rest asks for the section ("the milk")

    public static Kind Choose(float roll, bool missing, StoreLayout.Zone zone)
    {
        if (missing) return Kind.Missing;
        if (roll < ProductChance) return Kind.Product;
        if (roll < ProductChance + WhichAisleChance) return zone.IsAisle ? Kind.WhichAisle : Kind.Product;
        if (roll < ProductChance + WhichAisleChance + PlaceChance) return zone.IsAisle ? Kind.Aisle : Kind.Department;
        return Kind.Department;
    }

    // True when getting them anywhere into the zone answers the question.
    public static bool ZoneIsEnough(Kind kind) => kind == Kind.Aisle || kind == Kind.Department;

    static readonly string[] productLines =
    {
        "Excuse me - I'm looking for {0}.",
        "Sorry, I can't find {0} anywhere.",
        "Hi! Do you still have {0}?",
        "Excuse me - where do you keep {0}?",
        "I've been round twice and I still can't see {0}.",
    };
    static readonly string[] whichAisleLines =
    {
        "Which aisle would I find {0} in?",
        "Excuse me - what aisle is {0} in?",
        "Sorry, which aisle has {0}?",
    };
    static readonly string[] aisleLines =
    {
        "Excuse me - where's aisle {1}?",
        "Which way is aisle {1}? I need {2}.",
        "I'm looking for aisle {1} - {3}?",
    };
    static readonly string[] departmentLines =
    {
        "Where's {2}?",
        "Excuse me - which way to {2}?",
        "Sorry, where do you keep {2}?",
    };
    static readonly string[] missingLines =
    {
        "That shelf's empty. Have you got {0} anywhere else?",
        "I can't get {0} - the shelf's bare. Is there another?",
        "Is there {0} anywhere else? That shelf's empty.",
    };

    // {0} the product, {1} the aisle number, {2} what the section is called out loud ("the
    // tinned food"), {3} the aisle's sign name ("Tins & Jars").
    public static string Ask(Kind kind, ProductDef product, StoreLayout.Zone zone, int pick)
    {
        string[] lines;
        switch (kind)
        {
            case Kind.WhichAisle: lines = whichAisleLines; break;
            case Kind.Aisle: lines = aisleLines; break;
            case Kind.Department: lines = departmentLines; break;
            case Kind.Missing: lines = missingLines; break;
            default: lines = productLines; break;
        }
        return Fill(lines[Mod(pick, lines.Length)], product, zone);
    }

    public static string Thanks(Kind kind, ProductDef product, StoreLayout.Zone zone)
    {
        switch (kind)
        {
            case Kind.WhichAisle: return Fill("Aisle {1} - got it. Thanks!", product, zone);
            case Kind.Aisle: return Fill("Aisle {1} - lovely, I'll find it from here.", product, zone);
            case Kind.Department: return Fill("Ah, {2}! Thank you.", product, zone);
            case Kind.Missing: return Fill("Oh, there's {0}! You're a star.", product, zone);
            default: return Fill("Oh, {0}. There it is - thanks!", product, zone);
        }
    }

    // What they're after, in a few words, for the shift recording and the eval agent.
    public static string Wanted(Kind kind, ProductDef product, StoreLayout.Zone zone)
    {
        switch (kind)
        {
            case Kind.Aisle: return "aisle " + zone.Aisle;
            case Kind.Department: return zone.Spoken;
            default: return product != null ? product.Name : zone.Spoken;
        }
    }

    static string Fill(string line, ProductDef product, StoreLayout.Zone zone) =>
        string.Format(line, product != null ? product.Name : zone.Spoken, zone.Aisle, zone.Spoken, zone.Name);

    static int Mod(int a, int n) => ((a % n) + n) % n;
}
