#!/bin/bash

mono ./bin/Debug/bug-demo.exe server & 
sleep 1
mono ./bin/Debug/bug-demo.exe client &
