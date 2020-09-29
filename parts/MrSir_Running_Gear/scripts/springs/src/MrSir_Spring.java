// Author: MisterSir
// Developed: 2009
// File: MrSir_Spring.java

package java.game.parts.MrSir_Running_Gear.springs;

import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;

public class MrSir_Spring extends Spring{
	float old_length, length, default_length, max_length, min_length;
	float old_maxlength, old_minlength, old_restlength;
	int parentType = 0;
	String additionalDescription = "";
	String corner = "";
	float mass = 0.0;
	float restAdd = 0.050;
	
	public MrSir_Spring( int id ){
		super( id );
		/*force	= 20000.0*0.75;	// N/m
		damping	= 2000.0*0.75;	// N/(m/s)
		restlength = 0.39;	// m
		maxlength = 0.7;	// m
		minlength = 0.2;	// m
		designedMassOnWheel = 300.0;*/
		name = "Spring";
		prestige_calc_weight = 20.0;	
	}
	
	public void setSide(int i){
		if (i==0){
			corner = "front left";
		}else if (i==1){
			corner = "front right";
		}else if (i==2){
			corner = "rear left";
		}else if (i==3){
			corner = "rear right";
		}
	}
	
	public void setAdjustability(float min, float max, float len){
		default_length = len;
		min_length = len - min;
		max_length = len + max;
	}
	
	public void setParentType(int i){
		int renderType = getRenderType();
		int rootType = renderType-i;
		parentType = rootType;
	}

	public void calcStuffs(){
		damping = force * 0.001;
		value = brand_prestige_factor * (force/4000.0 + default_length*10.0);	
		name = name_prefix + " " + corner + " " + Float.toString(force,"%1.0f N/m")+" "+Float.toString(length, "%1.2f\"")+" spring";
		description = "It's a "+name+". It can be span up to "+Float.toString(maxlength*39.4, "%1.2f\"")+" and be compressed to "+Float.toString(minlength*39.4, "%1.2f\"")+". \n"+additionalDescription;
		restlength = Inch2Meter(length)+restAdd;
	}	
	
	public void load( File saveGame ){
		super.load( saveGame );
		int	save_ver = saveGame.readInt();
		if (save_ver >= 1){
			minlength = saveGame.readFloat();
			maxlength = saveGame.readFloat();
			restlength = saveGame.readFloat();
			length = (restlength-restAdd)*39.4;
		}
	}

	public void save( File saveGame ){
		super.save( saveGame );
		int	save_ver = 2;
		saveGame.write( save_ver );
		if (save_ver >= 1){
			saveGame.write( minlength );
			saveGame.write( maxlength );
			saveGame.write( restlength );
		}
	}
	
	//---------tuning
	public int isTuneable(){
		return 1;
	}

	public void buildTuningMenu( Menu m ){	
		old_maxlength = maxlength;
		old_minlength = minlength;
		old_restlength = restlength;
		old_length = length;
		m.addItem( "Ride Height", 1, length, min_length, max_length, (max_length-min_length)/0.01+1, null ).printValue("   %1.2f inches");
		m.addItem( "Reset to factory defaults",			0);	//this should always be with cmd=0
		GameLogic.player.car.wakeUp();
	}

	 public void endTuningSession( int cancelled ){
		if( cancelled ){
			maxlength = old_maxlength;
			minlength = old_minlength;
			restlength = old_restlength;
			length = old_length;
		}else{
			if (length != old_length)
				GameLogic.spendTime(5*60);
			getCar_LocalVersion();
			if (the_car)
				the_car.forceUpdate();
		}
	}

	public void handleMessage( Event m ){
		if( m.cmd == 0 ){
			length = default_length;
			restlength = Inch2Meter(length)+restAdd;
			maxlength = restlength + Inch2Meter(1.5);
			minlength = restlength - Inch2Meter(1.5);			
			m.gadget.osd.findGadget( this, 1 ).setValue( length );
		}else if( m.cmd == 1 ){
			length = ((Slider)m.gadget).value;
			restlength = Inch2Meter(length)+restAdd;
			maxlength = restlength + Inch2Meter(1.5);
			minlength = restlength - Inch2Meter(1.5);		
			m.gadget.osd.findGadget( this, 1 ).setValue( length );
		}
	}
}
