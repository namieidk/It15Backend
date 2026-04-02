using System.ComponentModel.DataAnnotations;

public class SystemSettings
{
    [Key]
    public int Id { get; set; }  

    public int SessionTimeout { get; set; } = 30;
    public int PasswordExpiry { get; set; } = 90;
    public bool MfaRequired { get; set; } = true;
    public bool AlertCritical { get; set; } = true;
    public bool AlertLogins { get; set; } = false;
    public bool AlertExports { get; set; } = true;
    public int StorageUsage { get; set; } = 84;
}