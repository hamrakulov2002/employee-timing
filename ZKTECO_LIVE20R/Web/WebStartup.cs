using System;
using System.Collections.Generic;
using System.Text;
using ZKTECO_LIVE20R.Data;

namespace ZKTECO_LIVE20R.Web;

public class WebStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddDbContext<AppDbContext>();

        services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(options =>
        {
            options.ViewLocationFormats.Clear();
            options.ViewLocationFormats.Add("/web/views/{1}/{0}.cshtml");
            options.ViewLocationFormats.Add("/web/views/Shared/{0}.cshtml");
        });
    }

    public void Configure(IApplicationBuilder app, IHostEnvironment env)
    {
        if(env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
        });
    }
}
