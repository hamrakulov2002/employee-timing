using ZKTECO_LIVE20R.Data;

namespace flow.web.Views
{
    public class WebStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
            services.AddDbContext<AppDbContext>();
        }
    }
}
