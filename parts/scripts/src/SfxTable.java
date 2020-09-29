package java.game.parts;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.cars.*;

public class SfxTable extends Native
{
//	public native void finalize( );	//ha _ref lesz
	public native int getItems( );
	public native void clear( );
	public native void addItem( ResourceRef sfx, float pitch, float pmin, float pmax, float vmin, float vmax );
	
	public native int AddItemPtr( ResourceRef sfx, float pitch, float pmin, float pmax, float vmin, float vmax );
	//these are surely valid offsets (from soundBaseOffset=AddItemPtr(..)):
	//RawEdit.setF(soundBaseOffset+0x10,1.0/*new_pitch*);
	//RawEdit.setF(soundBaseOffset+0x14,*unkown*);
	//RawEdit.setF(soundBaseOffset+0x18,*unkown*);
	//RawEdit.setF(soundBaseOffset+0x1C,*unkown*);
	//RawEdit.setF(soundBaseOffset+0x20,*unkown*);
}

