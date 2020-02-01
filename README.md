# Silent_Updater
Quite some time ago I needed to update a program on client machines, silently. To do that I built a tool that ran in the background and polled for a new version of the main client application. It then proceeded to silently update and install the new version on the clients.

## Algorithm sketch:
```
	- [x] A Process 1: start updater, 
	- [x] B Process 1: find new version,
		- [x] if	B1 = new version in %appdata%					
			    - [x] B10 really new version, check file version	
				- [x] B11 remove autostart entry (Entry: TARGET_x.y.z.w is removed)
				- [x] B12 start new updater (same as C2)
				- [x] B13 close this updater (basic return seems to suffice)
		- [x] or	B2 = new version online
				- [x] B20.1 get file binary version (current running exe)
				- [x] B20.2 compare online version w. local one	
				- [x] B20.3 download setup, extract binary and compare versions again
				- [x] B21 store it	
			Either only binaries or also new config files: 				
			- [x] Binary Update:
						** Precondition: binary has version number x' < x or y' < y or z' < z or w' < w	
						** Postcondition: binary has same version number x.y.z.w as was downloaded					
						** Postcondition: new path: 	C:\Users\%name%\AppData\Local\INSTALLLOCATION\x.y.z.w\	
						** Postcondition: new user.dat is overwritten by old user.dat							 			
					    ** Postcondition: contents of e.g. user.dat are:	See below for contents *0			
						** Postcondition: contents of folder are:	See below for contents *1					
			- [x] Config Update:
						** Precondition: configfile has version number x' < x or y' < y or z' < z or w' < w
						** Postcondition: configfile has same version number x.y.z.w as was downloaded		
						** Postcondition: new path: C:\Users\%name%\AppData\Local\INSTALLLOCATION\Target_x.y.z.w
						** Postcondition: new file client_check_x.y.z.w in new path								
						** Postcondition: only one client_check* file in new path								
		- [x] B22 start new updater 																			
		- [x] B23 close this updater // test it like this, but race condition									
	- [x] C Process 1: not find new version,																			
		- [x] C1 registry change to this version if not already this version	(Entry: TARGET_x.y.z.w is inserted)
		- [x] C2 start target: %appdata%\INSTALLLOCATION\x.y.z.w\target.exe  
		- [x] C3 start loop, update check									
```
### Contents:
```
*0 
<?xml version="1.0" encoding="utf-8"?>
<User xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
....
							</User>
							  	- C:\Users\user\AppData\Local\INSTALLLOCATION\x.y.z.w:
								- client_check_x.y.z.w (configfile)										
								- user.dat					    												
								- TargetClient          <-- file version stored in PE32
								- client_check.exe		<-- file version stored in PE32
								- NDde.dll				<-- excluded from versioning
								- RestSharp.dll			<-- excluded from versioning
								- cupdt.exe 			<-- file version stored in PE32
								- cupdt.manifest
								- README					
```
