namespace HeroicInventions.Sim.Electrics;

/// <summary>
/// What turns a generator's train: its <see cref="Name"/> (the key a bank files the charge under: wind, water-wheel, steam-jet, stirling),
/// the part's id, its speed, and the train between it and the generator's rotor: <see cref="Ratio"/> = ω_rotor / ω_prime (125 for three
/// 5:1 stages) and <see cref="Eta"/>, the share of the power that arrives (0.97³ for three meshes at 0.97). <see cref="Torque"/> is the
/// prime mover's driving torque at a speed, less its own load, where the sim has a curve for it (a windmill); else null.
/// </summary>
public sealed record PrimeMover(string Name, string? Id, Func<double>? Omega, double Ratio, double Eta, Func<double, double>? Torque);
