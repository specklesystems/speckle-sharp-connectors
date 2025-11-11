# Intro
The rhino importer is functionally made from two parts:
- The `Speckle.Importers.JobProcessor`: A windows background service, designed to always be running.
- The `Speckle.Importers.Rhino`: A commandline app triggered by the JobProcessor to handle a single job.

They are deployed as a single product, and share the Innosetup installer deployment system of connectors.

> Note: this is documentation for internal purposes only.
> Despite much of the code being open-source, we do not offer any support or documentation for self hosters.

# First Time Setup and Checking For Updates

On the production/staging Windows servers, you'll find a powershell script `InstallUpdate.ps1` that 
does a couple of things:
 - Ensures the latest version of the importer is installed
 - Defines the Postgres connection string, and how many service instances to stop/start (right now, just the 1 for prod)

`InstallUpdate.ps1` can be run to both check for updates and install updates, and to perform fresh installs and configuring the service(s)
The template for that script can be found [here](https://github.com/specklesystems/gitOps/blob/main/terraform/modules/windows-machine/templates/autoupdater/InstallUpdate.ps1.tpl) but needs to be tweaked per deployment.

There's lot more to do with the windows infra/rhino licence config that's not covered in this readme document.

# The Windows Service

You can view it from the (search "services" in search), It should be running at all times.
and can be stopped/started manually from the "services" menu

It does not display a terminal, the best place to see logs is in our seq-dev instance.

You can view error/crashes either via
1. Event viewer - there is already a "Custom View" called "Speckle Rhino Job Processor"
2. Reliability Monitor - Shows historical crash histogram
   Note, the `Speckle.Importers.Rhino` crashes a lot due to Rhino.Inside behaviour, it's handled
   crashes of the `Speckle.Importers.JobProcessor` are more serious.

If you need to troubleshoot, it is still possible to manually start the job processor exe as a command line app, rather than as a window service
this will give you back a terminal with all logs. But this is probably better done on the staging server...

Because it's a Windows Service, it will start up with the Windows OS.
If the process crashes (e.g. because it could not connect to the server/db)
it will automatically be restarted, with a backoff policy defined in the `InstallUpdate.ps1` script

If it's in a weird state where you need to manually restart it, this can be done from Services/task manager. Or simply by re-starting the machine.

# Tricking the InstallUpdate script into running even when there's no updates
If you want to make a change to the env vars of the service, or change the setup of the services in some way, it can be useful to run the script
without there actually being an update.

The easiest way to do this is to remove the registry key in `HKEY_CURRENT_USER\Software\Speckle\Services\InstalledConnectors`
That way, the script assumes that there is an update when ran.

# Debugging / Running Against a Local Server

If you have a local Speckle server and want to test processing Rhino jobs.
Firstly, there's a few things on the server you'll need to configure.
I'd recommend using the docker-compose files in the root of the server repo.

On speckle-frontend-2 set the envvar:
```yaml
    NUXT_PUBLIC_FF_RHINO_FILE_IMPORTER_ENABLED: 'true'
```

On speckle-server set the envvars:
```yaml
    FILEIMPORT_SERVICE_USE_PRIVATE_OBJECTS_SERVER_URL: 'false'
    FF_RHINO_FILE_IMPORTER_ENABLED: 'true'
    FILE_IMPORT_TIME_LIMIT_MIN: 30
```

Next, you can run the `Speckle.Importers.JobProcessor: Local Docker DB` launch configuration straight from your IDE.
The `launchSettings.json` should already be setup with the correct connection string.
It will run as normal CLI app, not a Windows Service, but aside from minor logging differences, this will work the same.

N.b. you will find you are not able to easily debug the `Speckle.Importers.Rhino` project like this, because it's a spawned sub process.
You have two options.
1. Capture the command line args and call it manually from your IDE, bypassing the JobProcessor.
2. Add a `Thread.Sleep(10000)` near the start of the entry point of the Rhino importer, and during the sleep time, attach your IDE to a running process.
