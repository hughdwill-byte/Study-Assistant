namespace StudyHud.Core.Models;

/// <summary>A symbol the user can click to insert, with a name and search keywords.</summary>
public sealed record EngineeringSymbol
{
    public required string Glyph { get; init; }
    public required string Name { get; init; }
    public string Keywords { get; init; } = "";
    public string Category { get; init; } = "";
    public bool IsCustom { get; init; }

    /// <summary>True if <paramref name="query"/> matches the glyph, name, or keywords (case-insensitive).</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        if (Glyph == q) return true;
        return Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || Keywords.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A user-added symbol, persisted in settings.</summary>
public sealed record CustomSymbol
{
    public required string Glyph { get; init; }
    public string Name { get; init; } = "";
}

/// <summary>
/// The built-in engineering symbol set for the symbol palette (Greek letters, maths/logic operators,
/// units, sub/superscripts). Curated so the common ones an engineer reaches for are one click away.
/// </summary>
public static class EngineeringSymbols
{
    public static IReadOnlyList<EngineeringSymbol> BuiltIn { get; } = new EngineeringSymbol[]
    {
        // ── Greek (lower) ────────────────────────────────────────────────────
        S("α", "alpha", "angle coefficient greek a", "Greek"),
        S("β", "beta", "greek b", "Greek"),
        S("γ", "gamma", "greek g", "Greek"),
        S("δ", "delta small", "change greek d", "Greek"),
        S("ε", "epsilon", "strain permittivity greek e", "Greek"),
        S("ζ", "zeta", "damping greek z", "Greek"),
        S("η", "eta", "efficiency greek", "Greek"),
        S("θ", "theta", "angle greek", "Greek"),
        S("κ", "kappa", "greek k", "Greek"),
        S("λ", "lambda", "wavelength greek l", "Greek"),
        S("μ", "mu", "micro friction permeability greek m", "Greek"),
        S("ν", "nu", "kinematic viscosity greek n", "Greek"),
        S("ξ", "xi", "greek", "Greek"),
        S("π", "pi", "3.14159 greek p", "Greek"),
        S("ρ", "rho", "density resistivity greek r", "Greek"),
        S("σ", "sigma", "stress conductivity std dev greek s", "Greek"),
        S("τ", "tau", "shear stress torque time constant greek t", "Greek"),
        S("φ", "phi", "phase angle greek f", "Greek"),
        S("χ", "chi", "greek", "Greek"),
        S("ψ", "psi", "greek", "Greek"),
        S("ω", "omega small", "angular frequency greek w", "Greek"),
        // ── Greek (upper) ────────────────────────────────────────────────────
        S("Δ", "Delta", "change difference greek", "Greek"),
        S("Σ", "Sigma", "sum summation greek", "Greek"),
        S("Ω", "Omega", "ohm resistance greek", "Greek"),
        S("Φ", "Phi", "flux greek", "Greek"),
        S("Θ", "Theta", "angle greek", "Greek"),
        S("Λ", "Lambda", "greek", "Greek"),
        S("Π", "Pi", "product greek", "Greek"),
        S("Ψ", "Psi", "greek", "Greek"),
        S("Γ", "Gamma", "greek", "Greek"),
        // ── Operators / maths ────────────────────────────────────────────────
        S("±", "plus-minus", "tolerance pm", "Maths"),
        S("∓", "minus-plus", "mp", "Maths"),
        S("×", "times", "multiply cross product", "Maths"),
        S("÷", "divide", "division obelus", "Maths"),
        S("⋅", "dot product", "multiply middot cdot", "Maths"),
        S("≈", "approximately equal", "approx almost", "Maths"),
        S("≠", "not equal", "neq", "Maths"),
        S("≡", "identical to", "equivalent congruent", "Maths"),
        S("≤", "less than or equal", "leq", "Maths"),
        S("≥", "greater than or equal", "geq", "Maths"),
        S("≪", "much less than", "ll", "Maths"),
        S("≫", "much greater than", "gg", "Maths"),
        S("∞", "infinity", "inf", "Maths"),
        S("√", "square root", "radical surd", "Maths"),
        S("∛", "cube root", "radical", "Maths"),
        S("∑", "summation", "sum sigma", "Maths"),
        S("∏", "product", "pi product", "Maths"),
        S("∫", "integral", "int", "Maths"),
        S("∮", "contour integral", "closed loop integral", "Maths"),
        S("∂", "partial derivative", "partial del", "Maths"),
        S("∇", "nabla", "del gradient divergence curl", "Maths"),
        S("∝", "proportional to", "propto", "Maths"),
        S("∴", "therefore", "hence", "Maths"),
        S("∵", "because", "since", "Maths"),
        S("∈", "element of", "in member", "Logic"),
        S("∉", "not element of", "not in", "Logic"),
        S("∀", "for all", "forall universal", "Logic"),
        S("∃", "there exists", "exists", "Logic"),
        S("⇒", "implies", "then arrow", "Logic"),
        S("⇔", "if and only if", "iff equivalent", "Logic"),
        S("→", "right arrow", "to yields", "Arrows"),
        S("←", "left arrow", "from", "Arrows"),
        S("↔", "left-right arrow", "both", "Arrows"),
        S("∠", "angle", "geometry", "Geometry"),
        S("∥", "parallel", "parallel to", "Geometry"),
        S("⊥", "perpendicular", "normal orthogonal", "Geometry"),
        S("∼", "similar to", "tilde approx", "Geometry"),
        S("⌀", "diameter", "phi dia round", "Geometry"),
        // ── Units / notation ─────────────────────────────────────────────────
        S("°", "degree", "deg angle temperature", "Units"),
        S("′", "prime", "minutes feet arcminute", "Units"),
        S("″", "double prime", "seconds inches arcsecond", "Units"),
        S("℃", "degrees celsius", "temperature centigrade", "Units"),
        S("℉", "degrees fahrenheit", "temperature", "Units"),
        S("Å", "angstrom", "length 1e-10", "Units"),
        S("µ", "micro", "micron 1e-6 mu", "Units"),
        S("℧", "mho", "siemens conductance inverse ohm", "Units"),
        S("Ω", "ohm", "resistance omega", "Units"),
        S("‰", "per mille", "permille per thousand", "Units"),
        S("∿", "alternating current", "ac sine wave", "Electrical"),
        S("⏚", "earth ground", "ground gnd electrical", "Electrical"),
        S("⊕", "circled plus", "xor direct sum", "Maths"),
        S("⊗", "circled times", "tensor cross", "Maths"),
        // ── Superscripts / subscripts ────────────────────────────────────────
        S("²", "superscript two", "squared power 2", "Scripts"),
        S("³", "superscript three", "cubed power 3", "Scripts"),
        S("¹", "superscript one", "power 1", "Scripts"),
        S("⁰", "superscript zero", "power 0", "Scripts"),
        S("ⁿ", "superscript n", "power n exponent", "Scripts"),
        S("⁻", "superscript minus", "negative exponent inverse", "Scripts"),
        S("₀", "subscript zero", "index 0", "Scripts"),
        S("₁", "subscript one", "index 1", "Scripts"),
        S("₂", "subscript two", "index 2", "Scripts"),
        S("₃", "subscript three", "index 3", "Scripts"),
        S("ₙ", "subscript n", "index n", "Scripts"),
        // ── Fractions ────────────────────────────────────────────────────────
        S("½", "one half", "fraction 1/2", "Fractions"),
        S("¼", "one quarter", "fraction 1/4", "Fractions"),
        S("¾", "three quarters", "fraction 3/4", "Fractions"),
        S("⅓", "one third", "fraction 1/3", "Fractions"),
    };

    private static EngineeringSymbol S(string glyph, string name, string keywords, string category)
        => new() { Glyph = glyph, Name = name, Keywords = keywords, Category = category };
}
