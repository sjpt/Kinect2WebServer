# kinectv2-webserver
This project gives web browser access via WebSockets to Kinect 2, Kinect Azure and Zed cameras.

It has been devleoped for use by the Organic (Mutator) system but could be used more widely.

It's primary goal is
access the cameras via their native SDKs, especially the 3d depth view
process and analyze that view to give a compacted 'core' representation
send the core representation to the browser runtime for use in user interactive dynamics.

It can also pass the raw depth view, 

It can be controlled from the browser code to select devices, resolutions, etc.


