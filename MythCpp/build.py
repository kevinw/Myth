import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "CppKit"))
os.chdir(os.path.dirname(os.path.abspath(__file__)))
import build as kit

kit.build("mythcpp", ["main.cpp"], args=sys.argv[1:])
