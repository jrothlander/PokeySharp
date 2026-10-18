// The headless checks, as a program: `dotnet run --project tests/Pokey.SelfTest`.
// Exit code 0 is a pass, 1 a failure, so CI needs nothing else.
return Pokey.SelfTest.SelfTest.Run();
