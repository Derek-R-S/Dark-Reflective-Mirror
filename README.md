# Dark Reflective Mirror

## What
Dark Reflective Mirror is a transport for Mirror Networking which relays network traffic through your own servers. This allows you to have clients host game servers and not worry about NAT/Port Forwarding, etc. There are still features I plan on adding but it still is completely usable, Some people in the Mirror discord are even using it in their games! Also note, If you see some thins that could use optimizations please tell me! I love to learn and would like to make this as optimized as possible. Thanks.

## Features
* WebGL Support, WebGL can host servers!
* Built in server list!
* Relay password to stop other games from stealing your precious relay!
* Relay supports connecting users without them needing to port forward!
* Direct Connect Support, Allows dedicated servers to use the built in server list, but not use any relay bandwidth!

## Plans

For the future I plan on adding features such as:
* STUN/ICE (Will try to do NAT Punch if ports are blocked)
* Multi Relay server setup for load balancing (It will split players up between multiple relay servers to make sure one single relay server isnt doing all the heavy lifting)
* Optimizations to improve performance

## How does it work?

You might be thinking, DarkRift2? Mirror? Arent they both competitors in the unity networking sector? Well you could argue to which one is better, but they are actually pretty much opposites. DarkRift2 is a low level, highly efficient networking system, while Mirror is a high level networking solution made to just get your game out there without worrying about netcode. So they actually work pretty well together. Anyways, it works like the following. You run a standalone DarkRift 2 server and use the relay plugin on it, then you make mirror use the Relay Transport and they communicate and share data! Easy as that.

## Known Issues/Flaws
This version doesnt implement STUN/ICE so with the direct connect module, if the ports arent open, it will fail to connect. Still good for dedicated servers though!

Disconnects from the relay will not auto reconnect **yet**. So a dedicated host is extremely recommended!

## Usage

Now for the juicy part, using it. Like I mentioned in the 'What' section, this is a prototype so if theres problems, please report them to me. Also PRs are also always welcomed! :)

First things first, you will need:
* Mirror, Install that from Asset Store or github.
* Download the latest release of Dark Reflective Mirror Unity Package and put that in your project also. Download from: [Releases](https://github.com/Derek-R-S/Dark-Reflective-Mirror/releases).

Then import Dark Rift 2 (Both the Paid and Free versions work fine!), You can get it from the Asset Store.

#### Setting up the unity project

The unity package, may already do this. If it does not then follow these steps: Open the tab by clicking Edit>Project Settings, then click the Script Execution Order tab. Click the plus in the bottom right and set DarkReflectiveMirrorTransport to 1002 and click apply.

#### Client Setup
Running a client is fairly straight forward, attach the DarkReflectiveMirrorTransport script to your NetworkManager and set it as the transport. Put in the IP/Port of your relay server (ignore the ip/port of the UnityClient script), assign DarkReflectiveMirror as the Transport on the NetworkManager. When you start a server, you can simply get the URI from the transport and use that to connect. If you wish to connect without the URI, the DarkReflectiveMirror component has a public "Server ID" field which is what clients would set as the address to connect to. 

If your relay server has a password, enter it in the relayPassword field or else you wont be able to connect. By default the relays have no password.

If you want to use websockets, simply toggle 'Use Websockets' under the transports inspector. Make sure the server is running Web Sockets as well!

##### Direct Connecting

Dark Reflective Mirror supports direct connecting for reduce relay usage. To enable Direct Connecting add the component 'Dark Mirror Direct Connect Module' to your network manager, then add any of the follow transports (Telepathy, LLAPI, Ignorance) and set it as the reference in the Direct Connect Module. And thats all there is to it, your dedicated servers will now use direct connect. Just make sure on the transport that 'Force Relay Traffic' is off or else it will ignore the direct connect module.

##### Server List

Dark Reflective Mirror has a built in room/server list if you would like to use it. To use it you need to set all the values in the 'Server Data' tab in the transport. Also if you would like to make the server show on the list, make sure "Show on server list" is checked. Once you create a server, you cannot update those values unless you restart the server.

To request the server list you need a reference to the DarkReflectiveMirrorTransport from your script and call 'RequestServerList()'. This will invoke a request to the server to update our server list. Once the response is recieved the field 'relayServerList' will be populated and you can get all the servers from there.
 
#### Free Relay
Note: If you would like to test without creating a server, feel free to use my test server with IP: 34.67.125.123 and port 4296 (For a free websockets relay, use port: 4297), no password. Which is in there by default  :)

#### Server Setup
DarkRift 2 includes a "DarkRift Server (.NET Framework/Core).zip" file in its assets, you will need that for the server. For this demonstration I will be explaining how to host the .NET framework version on a windows machine but running it on a linux machine is fairly straight forward and they have answered it in their discord plenty of times so you should be able to find an answer there. So take the server zip file and extract it to somewhere out of your unity project (Like your desktop in a folder). Download the Server Plugin from the [Releases](https://github.com/Derek-R-S/Dark-Reflective-Mirror/releases) and merge it with your DarkRift server folder. Plugins and Lib folder should merge and put the required files automatically in the right spot. Lastly run DarkRift.Server.Console.exe and it should say Relay Server Started. Then you are good! By default DarkRift server uses port 4296 UDP and TCP so make sure your server has those open. You can change those ports in the Server.config file.

If you would like to setup a password on your server you will need to put in the following into your server.config file, make sure to overwrite the existing plugins area in the config file.

```
  <plugins loadByDefault="true">
    <plugin type="RelayPlugin">
      <settings password="Put your password here!" />
    </plugin>
  </plugins>

```

\*The free version only includes a .NET framework server which from my benchmarks, uses way more performance(More than double!) than the .NET core server. Although you could compile your own .NET core server using the DarkRift.Server.dll from the free version, this requires looking at the functions and understanding how to start a server using it.


#### Web Socket Server Setup
To setup the server to use the Web Socket Listener, put everything from the WebSocketListener.zip into the Plugins folder of your server.

Open the Server.config file and modify the listeners section to be:

```
  <listeners>
    <listener name="WebSocketListener" type="WebSocketNetworkListener" address="0.0.0.0" port="4296">
      <settings noDelay="true" />
    </listener>
  </listeners>
```

## Example
Maqsoom made an example, feel free to check it out at: https://github.com/maqsoom/DarkReflectiveMirror-Example 

## Credits
Jamster - Been in contact and has helped sort out some Dark Rift issues. He also made DR2 :P

Maqsoom & JesusLuvsYooh - Both really active testers and have been testing it since I pitched the idea. They've found many issues and prototyped almost every single version!

[Fleck](https://github.com/statianzo/Fleck) - Fleck is used for the Web Socket server listener for Dark Rift 2.

[Flejmer](https://github.com/flejmer/) - Had an existing Web Socket listener for Dark Rift 2, That I forked off of and tailored specifically for this relay.

## License
[MIT](https://choosealicense.com/licenses/mit/)
