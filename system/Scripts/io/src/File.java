package java.io;

import java.util.resource.*;

public class File extends Native
{
	final static int MODE_READ = 0x00000000;
	final static int MODE_WRITE= 0x00000001;

	String	name;

	public File(){}

	public File( String n )
	{
		this();
		name=n;
	}

	public void finalize()
	{
		close();
	}

	//meg lehetne csinalni, hogy az open-close automatikusan hajtodjon vegre
	//attol fuggoen, hogy irni/olvasni akarunk
	public native int	open( int mode );
	public native void	close();

	public native int	write( int value );
	public native int	write( float value );
	public native int	write( ResourceRef value );
	public native int	write( String value );

	public native int		readInt();
	public native float		readFloat();
	public native int		readResID();
	public native String	readString();

	public native static int delete( String f );
	public native static int copy( String original, String copy );
	public native static int move( String original, String renamed );

	public static void delete( String path, String mask )
	{
		FindFile ff = new FindFile();
		String name=ff.first( path+mask );
		while( name )
		{
			delete( path+name );
			name = ff.next();
		}
		ff.close();
	}

	//non-recursive copy
	public static void copy( String path, String mask, String path2 )
	{
		FindFile ff = new FindFile();
		String name=ff.first( path+mask, FindFile.FILES_ONLY );
		while( name )
		{
			copy( path+name, path2+name );
			name = ff.next();
		}
		ff.close();
	}

	//non-recursive move
	public static void move( String path, String mask, String path2 )
	{
		FindFile ff = new FindFile();
		String name=ff.first( path+mask, FindFile.FILES_ONLY );
		while( name )
		{
			move( path+name, path2+name );
			name = ff.next();
		}
		ff.close();
	}

	public native static int exists( String f );
	public native static int TimeLow( String f );
	public native static int TimeHigh( String f );
}