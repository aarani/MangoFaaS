using System;

namespace MangoFaaS.Firecracker.Node.Pooling;

public class FirecrackerPoolOptions
{
    // Absolute path to the firecracker binary
    public string FirecrackerPath { get; set; } = "/usr/local/bin/firecracker";

    // Optional working directory for processes
    public string? WorkingDirectory { get; set; }

    // Default command-line arguments used for each process
    public string? DefaultArgs { get; set; }

    // Maximum number of concurrent firecracker processes
    public int MaxPoolSize { get; set; } = 4;

    // How long an idle process is kept alive before being terminated
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    // Max time to wait for process startup
    public int StartupTimeoutSeconds { get; set; } = 15;

    // Number of processes to prepare during startup
    public int PrepareCount { get; set; } = 5;

    // IP Subnet for allocation
    public string IpSubnet { get; set; } = "172.16.0.0/16";
}
