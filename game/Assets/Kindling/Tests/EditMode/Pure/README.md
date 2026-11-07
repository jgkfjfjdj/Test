Tests in `Pure/` must only use NUnit and the pure-C# assemblies (Core, Content, Simulation).
They are compiled twice: by Unity's Test Runner (EditMode) and by `sim/Kindling.Sim.Tests`
(`dotnet test`), so CI can run them without a Unity license. Keep to the NUnit 3
constraint model (`Assert.That`, `Assert.Throws`, `[TestCase]`) and C# 9.
