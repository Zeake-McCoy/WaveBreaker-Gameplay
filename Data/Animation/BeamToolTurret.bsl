@BlockID "BeamToolTurret"
@Version 2
@Author h

#--- Objects
using MissileTurretBase1 as subpart("MissileTurretBase1")
using MissileTurretBarrels as subpart("MissileTurretBarrels") parent MissileTurretBase1
using DrillLaserHead as subpart("DrillLaserHead") parent MissileTurretBarrels
using DrillTurretDoor1 as subpart("DrillTurretDoor1") parent MissileTurretBarrels
using DrillTurretDoor2 as subpart("DrillTurretDoor2") parent MissileTurretBarrels

var Extended = true
var Moving = false
var working = false

#--- Animations

func extend() {
        if (Extended == false) {
        if (Moving == false) {
        Moving = true
        DrillLaserHead.delay(10).movetoorigin(10, linear)
        DrillTurretDoor2.movetoorigin(10, linear)
        DrillTurretDoor1.movetoorigin(10, linear)
        API.StartLoop("thingextended", 10, 1, 21)
        }
              }
    }
func retract() {
        if (Extended == true) {
        if (Moving == false) {
        Moving = true
        reset()
        DrillLaserHead.translate([0, 0, -0.56], 10, linear)
        DrillTurretDoor2.delay(10).rotate([1, 0, 0], 22.8, 10, linear)
        DrillTurretDoor1.delay(10).rotate([1, 0, 0], -22.8, 10, linear)
        API.StartLoop("thingretracted", 10, 1, 21)
        }
              }
    }
func reset() {
        Extended = true
        DrillLaserHead.reset()
        DrillTurretDoor2.reset()
        DrillTurretDoor1.reset()
    }
func thingextended () {
        Moving = false
        Extended = true
    }
func thingretracted () {
        Moving = false
        Extended = false
    }
func saythelinekillerbeejak () {
        if (working == true) {
        extend()
        }
        if (working == false) {
        retract()
        }
}
    
#--- Actions
action block() {
    
    working() {
        working = true
        API.StartLoop("saythelinekillerbeejak", 10, -1)
    }
    notworking() {
        working = false
        API.StartLoop("saythelinekillerbeejak", 10, -1)
    }
}
    