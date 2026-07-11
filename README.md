# kinectv2-webserver
This project gives web browser access via WebSockets to Kinect 2, Kinect Azure and Zed cameras.

It has been devleoped for use by the Organic (Mutator) system but could be used more widely.
It gives a somewhat impressionistic view of what is seen by the camera.
This is suitable for crowded spaces,
especially with the camera mounted high so it can see well into the space.

It does low level processing to deteect edges (includidng depth edges) and motion.
It does not attempt higher level processing to identify features such as hands, fingers etc,
though it can pass a skeleton view (some cameras only) if that is provided by the camera SDK.

It's primary goal is
access the cameras via their native SDKs, especially the 3d depth view
process and analyze that view to give a compacted 'core' representation
send the core representation to the browser runtime for use in user interactive dynamics.

It can also pass the raw depth view, and in some cases the skeleton view.

It can be controlled from the browser code to select devices, resolutions, etc.


