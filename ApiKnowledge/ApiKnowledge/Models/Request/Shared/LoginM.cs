namespace ApiBusiness.Models.Request.Shared
{
    public class LoginM
    {
        public int pOpcion { get; set; } = 1;
        public string pUserName { get; set; } = string.Empty;
        public string pPass { get; set; } = string.Empty;
    }
}
